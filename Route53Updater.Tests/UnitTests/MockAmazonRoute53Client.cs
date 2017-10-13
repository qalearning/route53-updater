using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Route53Updater.Tests.UnitTests
{
    class MockAmazonRoute53Client : MockAmazonRoute53ClientBase
    {
        private ChangeBatch pubChange = CreateExpectedChangeBatch();
        private ChangeBatch privChange = CreateExpectedChangeBatch(createPublic: false);
        private ChangeBatch pubDelete = CreateExpectedChangeBatch(createDeleteChangeBatch: true);
        private ChangeBatch privDelete = CreateExpectedChangeBatch(createPublic: false, createDeleteChangeBatch: true);

        public bool PrivateRecordSetQueried { get; private set; }
        public bool PublicRecordSetQueried { get; private set; }
        public bool PrivateRecordSetChanged { get; private set; }
        public bool PublicRecordSetChanged { get; private set; }

        public int TimesChangeRecordSetCalled { get; private set; }
        public bool PublicRecordSetDeleted { get; private set; }
        public bool PrivateRecordSetDeleted { get; private set; }

        public bool ListResourceRecordSetsPublicCalled { get; private set; }
        public bool ListResourceRecordSetsPrivateCalled { get; private set; }
        public int TimesListResourceRecordSetsCalled { get; private set; }

        public override Task<ListHostedZonesByNameResponse> ListHostedZonesByNameAsync(ListHostedZonesByNameRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request.DNSName == TestConstants.FakePublicHostedZone)
            {
                PublicRecordSetQueried = true;
            }
            if (request.DNSName == TestConstants.FakePrivateHostedZone)
            {
                PrivateRecordSetQueried = true;
            }

            return Task.Run(() => CreateFakeListHostedZonesResponse());
        }

        public override Task<ChangeResourceRecordSetsResponse> ChangeResourceRecordSetsAsync(ChangeResourceRecordSetsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            TimesChangeRecordSetCalled++;
            if (request.HostedZoneId == TestConstants.FakeHostedZoneId && ChangeBatchesAreEquivalent(pubChange, request.ChangeBatch))
            {
                PublicRecordSetChanged = true;
            }
            if (request.HostedZoneId == TestConstants.FakeHostedZoneId && ChangeBatchesAreEquivalent(privChange, request.ChangeBatch))
            {
                PrivateRecordSetChanged = true;
            }

            if (request.HostedZoneId == TestConstants.FakeHostedZoneId && ChangeBatchesAreEquivalent(pubDelete, request.ChangeBatch))
            {
                PublicRecordSetDeleted = true;
            }
            if (request.HostedZoneId == TestConstants.FakeHostedZoneId && ChangeBatchesAreEquivalent(privDelete, request.ChangeBatch))
            {
                PrivateRecordSetDeleted = true;
            }

            return Task.Run(() => CreateFakeChangeResourceRecordSetResponse());
        }

        public override Task<ListResourceRecordSetsResponse> ListResourceRecordSetsAsync(ListResourceRecordSetsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            string ipAddress = string.Empty;
            if (request.HostedZoneId == TestConstants.FakeHostedZoneId)
            {
                if (request.StartRecordName == TestConstants.FakePublicDNSName)
                {
                    ipAddress = TestConstants.FakePublicIpAddress;
                    ListResourceRecordSetsPublicCalled = true;
                }
                else if (request.StartRecordName == TestConstants.FakePrivateDNSName)
                {
                    ipAddress = TestConstants.FakePrivateIpAddress;
                    ListResourceRecordSetsPrivateCalled = true;
                }
                TimesListResourceRecordSetsCalled++;
            }
            var fake = new ListResourceRecordSetsResponse
            {
                IsTruncated = false,
                ResourceRecordSets = new List<ResourceRecordSet>
                {
                   new ResourceRecordSet
                   {
                       Type = RRType.A,
                       Name = request.StartRecordName,
                       ResourceRecords = new List<ResourceRecord>
                       {
                           new ResourceRecord { Value = ipAddress}
                       }
                   }
                }
            };
            return Task.Run(() => fake);
        }

        private bool ChangeBatchesAreEquivalent(ChangeBatch expected, ChangeBatch actual)
        {
            if (expected.Changes.Count == actual.Changes.Count)
            {
                for (int i = 0; i < expected.Changes.Count; i++)
                {
                    var x = expected.Changes[i];
                    var y = actual.Changes[i];
                    if (x.Action == y.Action)
                    {
                        var xrrs = x.ResourceRecordSet;
                        var yrrs = y.ResourceRecordSet;
                        if (xrrs.Type == yrrs.Type &&
                            xrrs.Name == yrrs.Name &&
                            xrrs.TTL == yrrs.TTL &&
                            xrrs.ResourceRecords.Count == yrrs.ResourceRecords.Count)
                        {
                            return xrrs.ResourceRecords.TrueForAll(rr => yrrs.ResourceRecords.Count(yrr => yrr.Value == rr.Value) == 1);
                        }
                    }
                }
            }
            return false;
        }

        private static ChangeBatch CreateExpectedChangeBatch(bool createPublic = true, bool createDeleteChangeBatch = false)
        {
            string dnsName, address;
            if (createPublic)
            {
                dnsName = TestConstants.FakePublicDNSName;
                address = TestConstants.FakePublicIpAddress;
            }
            else
            {
                dnsName = TestConstants.FakePrivateDNSName;
                address = TestConstants.FakePrivateIpAddress;
            }

            ChangeAction expectedAction = createDeleteChangeBatch ? ChangeAction.DELETE : ChangeAction.UPSERT;

            return new ChangeBatch
            {
                Changes = new List<Change>
                {
                    new Change
                    {
                        Action = expectedAction,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Type = RRType.A,
                            Name = dnsName,
                            TTL = 300, //required but not obviously - the docs say it's optional.
                            ResourceRecords = new List<ResourceRecord>
                            {
                                new ResourceRecord
                                {
                                    Value = address
                                }
                            }
                        }
                    }
                }
            };
        }

        private ListHostedZonesByNameResponse CreateFakeListHostedZonesResponse()
        {
            return new ListHostedZonesByNameResponse
            {
                HostedZones = new List<HostedZone>
                {
                    new HostedZone { Id = TestConstants.FakeHostedZoneIdValue}
                }
            };
        }

        private ChangeResourceRecordSetsResponse CreateFakeChangeResourceRecordSetResponse()
        {
            return new ChangeResourceRecordSetsResponse
            {
                ChangeInfo = new ChangeInfo { Status = ChangeStatus.PENDING }
            };
        }

    }

    abstract class MockAmazonRoute53ClientBase : IAmazonRoute53
    {
        public virtual IClientConfig Config => throw new NotImplementedException();

        public virtual Task<AssociateVPCWithHostedZoneResponse> AssociateVPCWithHostedZoneAsync(AssociateVPCWithHostedZoneRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ChangeResourceRecordSetsResponse> ChangeResourceRecordSetsAsync(ChangeResourceRecordSetsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ChangeTagsForResourceResponse> ChangeTagsForResourceAsync(ChangeTagsForResourceRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateHealthCheckResponse> CreateHealthCheckAsync(CreateHealthCheckRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateHostedZoneResponse> CreateHostedZoneAsync(CreateHostedZoneRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateQueryLoggingConfigResponse> CreateQueryLoggingConfigAsync(CreateQueryLoggingConfigRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateReusableDelegationSetResponse> CreateReusableDelegationSetAsync(CreateReusableDelegationSetRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateTrafficPolicyResponse> CreateTrafficPolicyAsync(CreateTrafficPolicyRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateTrafficPolicyInstanceResponse> CreateTrafficPolicyInstanceAsync(CreateTrafficPolicyInstanceRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateTrafficPolicyVersionResponse> CreateTrafficPolicyVersionAsync(CreateTrafficPolicyVersionRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<CreateVPCAssociationAuthorizationResponse> CreateVPCAssociationAuthorizationAsync(CreateVPCAssociationAuthorizationRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteHealthCheckResponse> DeleteHealthCheckAsync(DeleteHealthCheckRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteHostedZoneResponse> DeleteHostedZoneAsync(DeleteHostedZoneRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteQueryLoggingConfigResponse> DeleteQueryLoggingConfigAsync(DeleteQueryLoggingConfigRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteReusableDelegationSetResponse> DeleteReusableDelegationSetAsync(DeleteReusableDelegationSetRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteTrafficPolicyResponse> DeleteTrafficPolicyAsync(DeleteTrafficPolicyRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteTrafficPolicyInstanceResponse> DeleteTrafficPolicyInstanceAsync(DeleteTrafficPolicyInstanceRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DeleteVPCAssociationAuthorizationResponse> DeleteVPCAssociationAuthorizationAsync(DeleteVPCAssociationAuthorizationRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DisassociateVPCFromHostedZoneResponse> DisassociateVPCFromHostedZoneAsync(DisassociateVPCFromHostedZoneRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetChangeResponse> GetChangeAsync(GetChangeRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetCheckerIpRangesResponse> GetCheckerIpRangesAsync(GetCheckerIpRangesRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetGeoLocationResponse> GetGeoLocationAsync(GetGeoLocationRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHealthCheckResponse> GetHealthCheckAsync(GetHealthCheckRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHealthCheckCountResponse> GetHealthCheckCountAsync(GetHealthCheckCountRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHealthCheckLastFailureReasonResponse> GetHealthCheckLastFailureReasonAsync(GetHealthCheckLastFailureReasonRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHealthCheckStatusResponse> GetHealthCheckStatusAsync(GetHealthCheckStatusRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHostedZoneResponse> GetHostedZoneAsync(GetHostedZoneRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHostedZoneCountResponse> GetHostedZoneCountAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetHostedZoneCountResponse> GetHostedZoneCountAsync(GetHostedZoneCountRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetQueryLoggingConfigResponse> GetQueryLoggingConfigAsync(GetQueryLoggingConfigRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetReusableDelegationSetResponse> GetReusableDelegationSetAsync(GetReusableDelegationSetRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetTrafficPolicyResponse> GetTrafficPolicyAsync(GetTrafficPolicyRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetTrafficPolicyInstanceResponse> GetTrafficPolicyInstanceAsync(GetTrafficPolicyInstanceRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<GetTrafficPolicyInstanceCountResponse> GetTrafficPolicyInstanceCountAsync(GetTrafficPolicyInstanceCountRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListGeoLocationsResponse> ListGeoLocationsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListGeoLocationsResponse> ListGeoLocationsAsync(ListGeoLocationsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListHealthChecksResponse> ListHealthChecksAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListHealthChecksResponse> ListHealthChecksAsync(ListHealthChecksRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListHostedZonesResponse> ListHostedZonesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListHostedZonesResponse> ListHostedZonesAsync(ListHostedZonesRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListHostedZonesByNameResponse> ListHostedZonesByNameAsync(ListHostedZonesByNameRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListQueryLoggingConfigsResponse> ListQueryLoggingConfigsAsync(ListQueryLoggingConfigsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListResourceRecordSetsResponse> ListResourceRecordSetsAsync(ListResourceRecordSetsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListReusableDelegationSetsResponse> ListReusableDelegationSetsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListReusableDelegationSetsResponse> ListReusableDelegationSetsAsync(ListReusableDelegationSetsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTagsForResourceResponse> ListTagsForResourceAsync(ListTagsForResourceRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTagsForResourcesResponse> ListTagsForResourcesAsync(ListTagsForResourcesRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTrafficPoliciesResponse> ListTrafficPoliciesAsync(ListTrafficPoliciesRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTrafficPolicyInstancesResponse> ListTrafficPolicyInstancesAsync(ListTrafficPolicyInstancesRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTrafficPolicyInstancesByHostedZoneResponse> ListTrafficPolicyInstancesByHostedZoneAsync(ListTrafficPolicyInstancesByHostedZoneRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTrafficPolicyInstancesByPolicyResponse> ListTrafficPolicyInstancesByPolicyAsync(ListTrafficPolicyInstancesByPolicyRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListTrafficPolicyVersionsResponse> ListTrafficPolicyVersionsAsync(ListTrafficPolicyVersionsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<ListVPCAssociationAuthorizationsResponse> ListVPCAssociationAuthorizationsAsync(ListVPCAssociationAuthorizationsRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<TestDNSAnswerResponse> TestDNSAnswerAsync(TestDNSAnswerRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<UpdateHealthCheckResponse> UpdateHealthCheckAsync(UpdateHealthCheckRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<UpdateHostedZoneCommentResponse> UpdateHostedZoneCommentAsync(UpdateHostedZoneCommentRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<UpdateTrafficPolicyCommentResponse> UpdateTrafficPolicyCommentAsync(UpdateTrafficPolicyCommentRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<UpdateTrafficPolicyInstanceResponse> UpdateTrafficPolicyInstanceAsync(UpdateTrafficPolicyInstanceRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }
    }
}
