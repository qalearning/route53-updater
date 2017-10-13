using System.Linq;

namespace Route53Updater.Tests.UnitTests
{
    class TestConstants
    {
        public const string FakeInstanceId = "i-abcd1111";
        public const string FakePublicDNSName = "server.example.com";
        public const string FakePrivateDNSName = "server.internal.corp";
        public const string FakeHostedZoneId = "I15AFAKE1DOKHM";
        public const string FakeHostedZoneIdValue = "/hostedzone/" + FakeHostedZoneId;
        public const string FakePublicIpAddress = "50.1.1.1";
        public const string FakePrivateIpAddress = "10.0.0.123";
        public static readonly string FakePublicHostedZone = string.Join(".", FakePublicDNSName.Split('.').Skip(1));
        public static readonly string FakePrivateHostedZone = string.Join(".", FakePrivateDNSName.Split('.').Skip(1));
    }
}