namespace Route53Updater
{
    public class CloudWatchEC2StateChangeEvent
    {
        public string Version { get; set; }
        public string Id { get; set; }
        public string DetailType { get; set; }
        public string Source { get; set; }
        public string Account { get; set; }
        public string Time { get; set; }
        public string Region { get; set; }
        public string[] Resources { get; set; }
        public EventDetail Detail { get; set; }
        public class EventDetail
        {
            [Newtonsoft.Json.JsonProperty("instance-id")]
            public string InstanceId { get; set; }
            public string State { get; set; }
        }
    }

}