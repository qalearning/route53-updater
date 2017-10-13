using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Amazon.EC2.Model;
using Amazon.Route53.Model;
using Amazon.Route53;
using Amazon.EC2;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Route53Updater
{
    public class Function
    {
        public const string PublicDNSTagKey = "PublicDNS";
        public const string PrivateDNSTagKey = "PrivateDNS";
        private const string StateRunning = "running";
        private const string StateStopping = "stopping";
        private const string StateTerminated = "terminated";
        private ILambdaLogger log;

        private enum UpdateDeleteIgnoreDecision
        {
            Update, Delete, Ignore
        }

        public async Task FunctionHandler(CloudWatchEC2StateChangeEvent input, ILambdaContext context)
        {
            log = context.Logger;
            log.LogLine(JsonConvert.SerializeObject(input));

            string instanceId = input.Detail.InstanceId;
            Instance instance = await GetInstanceDetails(instanceId);
            var tags = instance.Tags;
            log.LogLine($"Got tag keys: {string.Join(";", tags.Select(t => t.Key))}");

            string action = input.Detail.State.ToLower();

            await UpdateRoute53(instanceId, instance, tags, action);
        }

        private async Task UpdateRoute53(string instanceId, Instance instance, List<Amazon.EC2.Model.Tag> tags, string action)
        {
            log.LogLine($"Checking whether to update, delete or ignore");
            foreach (var tag in tags)
            {
                string key = tag.Key;
                if (key == PublicDNSTagKey || key == PrivateDNSTagKey)
                {
                    log.LogLine($"Got one! {key}");

                    bool isPublicDNS = key == PublicDNSTagKey;

                    UpdateDeleteIgnoreDecision decision = MakeDeleteUpdateOrIgnoreDecision(action, isPublicDNS);
                    if (decision == UpdateDeleteIgnoreDecision.Ignore)
                    {
                        log.LogLine($"Ignoring {action} for {instanceId}");
                        continue;
                    }

                    string dnsName = tag.Value;
                    string message = string.Empty;
                    if (decision == UpdateDeleteIgnoreDecision.Update)
                    {
                        message = "Creating / updating";
                    }
                    else if (decision == UpdateDeleteIgnoreDecision.Delete)
                    {
                        message = "Deleting";
                    }
                    log.LogLine($"{message} record set for {instanceId} - {dnsName}");
                    string id = GetHostedZoneIdFor(dnsName);
                    string ipAddress = isPublicDNS ? instance.PublicIpAddress : instance.PrivateIpAddress;
                    ChangeResourceRecordSetsRequest rreq = await CreateChangeResourceRecordSetsRequest(id, dnsName, ipAddress, decision);
                    if (rreq == null)
                    {
                        log.LogLine("Couldn't update. Skipping this one.");
                        continue;
                    }
                    try
                    {
                        var rresp = await R53.ChangeResourceRecordSetsAsync(rreq);
                        log.LogLine($"Result: {rresp.ChangeInfo.Status}");
                    }
                    catch (AggregateException ex)
                    {
                        foreach (var exp in ex.InnerExceptions)
                        {
                            log.LogLine(exp.ToString());
                        }
                    }
                }
            }
        }

        private UpdateDeleteIgnoreDecision MakeDeleteUpdateOrIgnoreDecision(string action, bool isPublicDNS)
        {
            if (action != StateRunning)
            {
                //delete on terminate for PrivateDNS, delete on stop or terminate for PublicDNS.
                // parameterise with an environment variable?
                if (isPublicDNS)
                {
                    return UpdateDeleteIgnoreDecision.Delete;
                }
                else if (action == StateTerminated)
                {
                    return UpdateDeleteIgnoreDecision.Delete;
                }
                else
                {
                    return UpdateDeleteIgnoreDecision.Ignore;
                }
            }
            return UpdateDeleteIgnoreDecision.Update;
        }

        private async Task<Instance> GetInstanceDetails(string instanceId)
        {
            var req = new DescribeInstancesRequest
            {
                InstanceIds = new List<string> { instanceId }
            };
            log.LogLine($"Getting tags for {instanceId}");
            var response = await EC2.DescribeInstancesAsync(req);

            Instance instance = response.Reservations[0].Instances[0];
            return instance;
        }

        private async Task<ChangeResourceRecordSetsRequest> CreateChangeResourceRecordSetsRequest(string id, string dnsName, string ipAddress, UpdateDeleteIgnoreDecision decision)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                log.LogLine($"No ip address provided. Looking up by hosted zone / dnsname.");
                ipAddress = await LookupIpAddressByZoneIdAndDNSName(id, dnsName);
                if (string.IsNullOrEmpty(ipAddress))
                {
                    log.LogLine($"No record set found for {id} - {dnsName}. Looks like it's been deleted already.");
                    return null;
                }
            }
            log.LogLine($"Creating {decision} change request for {dnsName} - {ipAddress} - {id}");
            ChangeAction action = (decision == UpdateDeleteIgnoreDecision.Delete) ? ChangeAction.DELETE : ChangeAction.UPSERT;

            var result = new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = id,
                ChangeBatch = new ChangeBatch
                {
                    Changes = new List<Change>
                     {
                         new Change
                         {
                              Action = action,
                              ResourceRecordSet = new ResourceRecordSet
                              {
                                   Name = dnsName,
                                   ResourceRecords = new List<ResourceRecord>
                                   {
                                        new ResourceRecord { Value = ipAddress}
                                   },
                                   Type = RRType.A,
                                   TTL = 300
                              }
                         }
                     }
                }
            };
            log.LogLine("Returning:");
            log.LogLine(JsonConvert.SerializeObject(result));
            return result;
        }

        private async Task<string> LookupIpAddressByZoneIdAndDNSName(string id, string dnsName)
        {
            var request = new ListResourceRecordSetsRequest
            {
                HostedZoneId = id,
                StartRecordName = dnsName
            };
            var response = await R53.ListResourceRecordSetsAsync(request);
            if (response.ResourceRecordSets.Count == 0 || response.ResourceRecordSets[0].ResourceRecords.Count == 0)
            {
                return string.Empty;
            }
            return response.ResourceRecordSets[0].ResourceRecords[0].Value;
        }

        private string GetHostedZoneIdFor(string dnsName)
        {
            log.LogLine($"Getting hosted zone id for {dnsName}");
            var zoneName = string.Join(".", dnsName.Split('.').Skip(1));
            var req = new ListHostedZonesByNameRequest
            {
                DNSName = zoneName
            };

            var response = R53.ListHostedZonesByNameAsync(req);
            try
            {
                response.Wait();
            }
            catch (AggregateException ex)
            {
                foreach (var exp in ex.InnerExceptions)
                {
                    log.LogLine(exp.ToString());
                }
            }
            string id = response.Result.HostedZones[0].Id;
            string result = id.Split('/')[2];
            log.LogLine($"Returning {result}");
            return result;
        }

        /// <summary>
        /// property injection
        /// </summary>
        public IAmazonEC2 EC2
        {
            get
            {
                if (ec2 == null)
                {
                    ec2 = new AmazonEC2Client();
                }
                return ec2;
            }

            set { ec2 = value; }
        }
        private IAmazonEC2 ec2;

        private IAmazonRoute53 r53;

        public IAmazonRoute53 R53
        {
            get
            {
                if (r53 == null)
                {
                    var cfg = new AmazonRoute53Config
                    {
                        LogResponse = true
                    };
                    r53 = new AmazonRoute53Client(cfg);
                }
                return r53;
            }
            set { r53 = value; }
        }
    }
}
