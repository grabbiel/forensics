// Linux App Service plan and the API web app (.NET 10, system-assigned identity, HTTPS only).
param location string
param tags object
param planName string
param webAppName string
param sku string
param appInsightsName string
param logAnalyticsWorkspaceId string
param sqlServerFqdn string
param sqlDatabaseName string
param keyVaultName string
param corsOrigin string

// App Service resolves these at runtime with the app's identity; the secrets are set after provisioning.
var jwtSigningKeyRef = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Jwt--SigningKey)'
var integrityKeyRef = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Integrity--Keys--k1)'

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: sku }
  properties: {
    reserved: true // Linux
  }
}

resource webApp 'Microsoft.Web/sites@2025-03-01' = {
  name: webAppName
  location: location
  // azd finds the deploy target for the `api` service by this tag.
  tags: union(tags, { 'azd-service-name': 'api' })
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/api/v1/health/live'
      appSettings: concat(
        [
          { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
          // Codeless agent until the app ships its own OpenTelemetry exporter; then remove it.
          { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
          { name: 'XDT_MicrosoftApplicationInsights_Mode', value: 'recommended' }
          {
            name: 'ConnectionStrings__Default'
            value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;'
          }
          { name: 'Jwt__SigningKey', value: jwtSigningKeyRef }
          { name: 'Integrity__ActiveKeyId', value: 'k1' }
          { name: 'Integrity__Keys__k1', value: integrityKeyRef }
        ],
        empty(corsOrigin) ? [] : [{ name: 'Cors__Origins__0', value: corsOrigin }]
      )
    }
  }
}

// Deploys use Entra tokens; username/password publishing stays off.
resource ftpCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01' = {
  parent: webApp
  name: 'ftp'
  properties: { allow: false }
}

resource scmCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01' = {
  parent: webApp
  name: 'scm'
  properties: { allow: false }
}

// The only stable alternative is 2016-09-01; this preview is the version Azure's own samples use.
#disable-next-line use-recent-api-versions
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: webApp
  name: 'to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServicePlatformLogs', enabled: true }
    ]
  }
}

output name string = webApp.name
output uri string = 'https://${webApp.properties.defaultHostName}'
output principalId string = webApp.identity.principalId
// IPv4 only: SQL firewall rules take IPv4 ranges.
output possibleOutboundIps array = filter(split(webApp.properties.possibleOutboundIpAddresses, ','), ip => !contains(ip, ':'))
