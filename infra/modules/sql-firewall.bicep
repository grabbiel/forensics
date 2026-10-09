// One SQL firewall rule per web app outbound IPv4 address, named after the address.
// A module, because the IP list is only known at deploy time and loops need their length when they start.
// Rules for IPs the app no longer uses are not removed; infra/README.md shows the cleanup.
param serverName string
param ipAddresses array

resource server 'Microsoft.Sql/servers@2025-01-01' existing = {
  name: serverName
}

resource rules 'Microsoft.Sql/servers/firewallRules@2025-01-01' = [
  for ip in ipAddresses: {
    parent: server
    name: 'app-outbound-${replace(ip, '.', '-')}'
    properties: {
      startIpAddress: ip
      endIpAddress: ip
    }
  }
]
