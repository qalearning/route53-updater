using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;

using Route53Updater;
using System.Text;
using Newtonsoft.Json;

namespace Route53Updater.Tests.UnitTests
{
    public class Route53UpdaterTests
    {
        private MockAmazonEC2Client mockEC2;
        private MockAmazonRoute53Client mockR53;
        private Function sut;
        private StringBuilder logBuffer;
        private ILambdaContext context;

        public Route53UpdaterTests()
        {
            mockEC2 = new MockAmazonEC2Client();
            mockR53 = new MockAmazonRoute53Client();
            sut = new Function
            {
                EC2 = mockEC2,
                R53 = mockR53
            };
            context = new TestLambdaContext();
            logBuffer = (context.Logger as TestLambdaLogger).Buffer;
        }

        [Fact]
        public void InvokesDescribeInstances()
        {
            var t = sut.FunctionHandler(RunningEvent, context);
            t.Wait();

            Assert.True(mockEC2.DescribeInstancesCalled);
        }

        [Fact]
        public void InvokesDescribeInstancesWithInstanceId()
        {
            var t = sut.FunctionHandler(RunningEvent, context);
            t.Wait();

            Assert.True(mockEC2.DescribeInstancesCalledWithSpecificId);
            Assert.Contains("Getting tags for " + TestConstants.FakeInstanceId, logBuffer.ToString());
        }

        [Fact]
        public void QueriesRoute53HostedZones()
        {
            var t = sut.FunctionHandler(RunningEvent, context);
            t.Wait();

            Assert.True(mockEC2.DescribeInstancesCalledWithSpecificId);

            Assert.True(mockR53.PublicRecordSetQueried);
            Assert.True(mockR53.PrivateRecordSetQueried);

            Assert.Contains("Getting hosted zone id for " + TestConstants.FakePrivateDNSName, logBuffer.ToString());
            Assert.Contains("Getting hosted zone id for " + TestConstants.FakePublicDNSName, logBuffer.ToString());
        }

        [Fact]
        public void UpdatesRoute53RecordSets()
        {
            var t = sut.FunctionHandler(RunningEvent, context);
            t.Wait();

            Assert.True(mockEC2.DescribeInstancesCalledWithSpecificId);

            Assert.True(mockR53.PublicRecordSetChanged);
            Assert.True(mockR53.PrivateRecordSetChanged);
            Assert.Equal(2, mockR53.TimesChangeRecordSetCalled);
        }

        [Fact]
        public void DeletesPublicRoute53RecordSetsOnStop()
        {
            var t = sut.FunctionHandler(StoppingEvent, context);
            t.Wait();

            Assert.True(mockR53.PublicRecordSetDeleted);
            Assert.Equal(1, mockR53.TimesChangeRecordSetCalled);
        }

        [Fact]
        public void DoesNotDeletePrivateRoute53RecordSetsOnStop()
        {
            var t = sut.FunctionHandler(StoppingEvent, context);
            t.Wait();

            Assert.False(mockR53.PrivateRecordSetDeleted);
        }

        [Fact]
        public void DeletesPrivateRoute53RecordSetsOnTerminate()
        {
            var t = sut.FunctionHandler(TerminatedEvent, context);
            t.Wait();

            Assert.True(mockR53.PrivateRecordSetDeleted);
            Assert.True(mockR53.PublicRecordSetDeleted);
            Assert.Equal(2, mockR53.TimesChangeRecordSetCalled);
        }

        [Fact]
        public void QueryByRecordSetEntryWhenIpAddressNotAvailable()
        {
            mockEC2.ReturnEmptyStringsForIpAddresses = true;
            var t = sut.FunctionHandler(TerminatedEvent, context);
            t.Wait();

            Assert.True(mockR53.ListResourceRecordSetsPublicCalled, "didn't ask for public record set");
            Assert.True(mockR53.ListResourceRecordSetsPrivateCalled, "didn't ask for private record set");
            Assert.Equal(2, mockR53.TimesListResourceRecordSetsCalled);
            Assert.True(mockR53.PrivateRecordSetDeleted, "didn't delete private record set");
            Assert.True(mockR53.PublicRecordSetDeleted, "didn't delete public record set");
            Assert.Equal(2, mockR53.TimesChangeRecordSetCalled);

        }

        private static CloudWatchEC2StateChangeEvent runningEvent;

        private static CloudWatchEC2StateChangeEvent RunningEvent
        {
            get
            {
                if (runningEvent == null)
                {
                    runningEvent = JsonConvert.DeserializeObject<CloudWatchEC2StateChangeEvent>(SampleEvents.RunningEvent);
                }
                return runningEvent;
            }
        }

        private static CloudWatchEC2StateChangeEvent stoppingEvent;

        private static CloudWatchEC2StateChangeEvent StoppingEvent
        {
            get
            {
                if (stoppingEvent == null)
                {
                    stoppingEvent = JsonConvert.DeserializeObject<CloudWatchEC2StateChangeEvent>(SampleEvents.StoppingEvent);
                }
                return stoppingEvent;
            }
        }

        private static CloudWatchEC2StateChangeEvent terminatedEvent;

        public static CloudWatchEC2StateChangeEvent TerminatedEvent
        {
            get
            {
                if (terminatedEvent == null)
                {
                    terminatedEvent = JsonConvert.DeserializeObject<CloudWatchEC2StateChangeEvent>(SampleEvents.TerminatedEvent);
                }
                return terminatedEvent;
            }
        }
    }
}
