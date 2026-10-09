// Azure SQL logical server with Entra-only authentication (no SQL passwords) and one DTU database.
param location string
param tags object
param serverName string
param databaseName string
param databaseSku string
param adminLogin string
param adminObjectId string
param adminPrincipalType string
param allowAllAzureServices bool

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true // required at creation, since there is no SQL admin password
      login: adminLogin
      sid: adminObjectId
      principalType: adminPrincipalType
      tenantId: tenant().tenantId
    }
  }
}

// The server API ignores azureADOnlyAuthentication on updates; this child resource keeps it enforced.
resource entraOnly 'Microsoft.Sql/servers/azureADOnlyAuthentications@2025-01-01' = {
  parent: server
  name: 'Default'
  properties: { azureADOnlyAuthentication: true }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: databaseSku
    tier: databaseSku == 'Basic' ? 'Basic' : 'Standard'
  }
  properties: {
    // Locally redundant backups: enough for a demo, cheaper than the geo default.
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// 0.0.0.0 is Azure's sentinel for "allow Azure services"; it admits every tenant's Azure IPs.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2025-01-01' = if (allowAllAzureServices) {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
