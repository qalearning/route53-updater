# route53-updater
Lambda function to automatically update Route53 entries for EC2 instances based on tags.

It'll need to run using an IAM role that allows EC2 describe-instances commands and pretty much full Route53 permissions.

Configure a CloudWatch Events Rule for EC2 state changes, running, stopping and terminating, to trigger the Lambda.

EC2 instances tagged with a `PublicDNS` tag with a value of e.g. `www.example.com` will have an A record created / updated pointing that DNS name to their Public IP Address when they go into a running state. When they are stopped or terminated, it will be deleted.

Instances tagged with a `PrivateDNS` tag with a value of e.g. `dev.internal.corp` will get an A record upserted on running, nothing on stopping, and deleted on terminated.

There's a lot more error handling to be added and I can already see a need for a conflict resolution module, for what happens if the record already exists and I'm launching a new instance.

It's basic for now but it works for me.
