param serverName string
param ruleNamePrefix string
param outboundIpAddresses array

resource server 'Microsoft.Sql/servers@2023-08-01' existing = {
  name: serverName
}

resource firewallRules 'Microsoft.Sql/servers/firewallRules@2023-08-01' = [for (ip, index) in outboundIpAddresses: {
  parent: server
  name: '${ruleNamePrefix}-api-outbound-${index}'
  properties: {
    startIpAddress: ip
    endIpAddress: ip
  }
}]
