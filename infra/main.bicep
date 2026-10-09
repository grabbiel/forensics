// Evidence Chain infrastructure: one resource group per azd environment.
// App Service (API) + Azure SQL (Entra-only) + Key Vault + Log Analytics/App Insights. The SPA lives on Vercel.
targetScope = 'subscription'

@minLength(1)
@maxLength(10)
@description('azd environment name, e.g. demo or prod. Part of every resource name.')
param environmentName string

@minLength(1)
@description('Azure region for every resource.')
param location string

@description('Object id of the person running azd: SQL Entra admin and Key Vault Secrets Officer.')
param principalId string

@description('Display label for the SQL Entra admin, usually the UPN of principalId.')
param sqlAdminLogin string

@allowed(['User', 'Group'])
@description('Kind of principal in principalId.')
param principalType string = 'User'

@description('Vercel production origin allowed by CORS, e.g. https://evidence-chain.vercel.app. Empty until known.')
param corsOrigin string = ''

@allowed(['B1', 'B2', 'B3', 'S1', 'P0v3', 'P1v3'])
@description('App Service plan SKU: B1 for the demo, B2 in the production design.')
param appServiceSku string = 'B1'

@allowed(['Basic', 'S0', 'S1', 'S2', 'S3'])
@description('Azure SQL DTU SKU: S1 for the demo, S2 in the production design.')
param sqlDatabaseSku string = 'S1'

@allowed(['AppOutboundIps', 'AllAzureServices'])
@description('SQL firewall: only the web app\'s outbound IPs, or every Azure service (any tenant).')
param sqlNetworkAccess string = 'AppOutboundIps'

@allowed(['Enabled', 'Disabled'])
@description('Key Vault purge protection. Irreversible once on: a deleted vault keeps its name for the retention period.')
param keyVaultPurgeProtection string = 'Disabled'

@description('Email for the monthly budget alert. Empty skips the budget.')
param budgetAlertEmail string = ''

@minValue(10)
@description('Monthly budget in USD for the resource group.')
param budgetAmount int = 60

@description('Budget start (first of a month, ISO 8601), pinned once so re-provisioning never moves it. Empty means this month.')
param budgetStartDate string = ''

@description('Do not set: supplies this month as the budget start when budgetStartDate is empty.')
param budgetFallbackStartDate string = utcNow('yyyy-MM-01T00:00:00Z')

// Same inputs, same names: re-provisioning updates in place.
var token = take(toLower(uniqueString(subscription().id, environmentName, location)), 6)
var tags = { 'azd-env-name': environmentName, app: 'evidence-chain' }
var names = {
  resourceGroup: 'rg-evidencechain-${environmentName}'
  logAnalytics: 'log-evidencechain-${environmentName}'
  appInsights: 'appi-evidencechain-${environmentName}'
  plan: 'asp-evidencechain-${environmentName}'
  webApp: 'app-evidencechain-${environmentName}-${token}'
  sqlServer: 'sql-evidencechain-${environmentName}-${token}'
  sqlDatabase: 'sqldb-evidencechain'
  keyVault: 'kv-ec-${environmentName}-${token}'
}

resource rg 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: names.resourceGroup
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  params: {
    location: location
    tags: tags
    logAnalyticsName: names.logAnalytics
    appInsightsName: names.appInsights
  }
}

module sql 'modules/sql.bicep' = {
  scope: rg
  params: {
    location: location
    tags: tags
    serverName: names.sqlServer
    databaseName: names.sqlDatabase
    databaseSku: sqlDatabaseSku
    adminLogin: sqlAdminLogin
    adminObjectId: principalId
    adminPrincipalType: principalType
    allowAllAzureServices: sqlNetworkAccess == 'AllAzureServices'
  }
}

module keyVault 'modules/keyvault.bicep' = {
  scope: rg
  params: {
    location: location
    tags: tags
    name: names.keyVault
    purgeProtection: keyVaultPurgeProtection == 'Enabled'
    officerPrincipalId: principalId
    officerPrincipalType: principalType
  }
}

module api 'modules/appservice.bicep' = {
  scope: rg
  params: {
    location: location
    tags: tags
    planName: names.plan
    webAppName: names.webApp
    sku: appServiceSku
    appInsightsName: monitoring.outputs.appInsightsName
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: names.sqlDatabase
    keyVaultName: keyVault.outputs.name
    corsOrigin: corsOrigin
  }
}

// After the app exists: its identity reads secrets, and its outbound IPs reach SQL.
module apiSecrets 'modules/keyvault-access.bicep' = {
  scope: rg
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: api.outputs.principalId
  }
}

module apiFirewall 'modules/sql-firewall.bicep' = if (sqlNetworkAccess == 'AppOutboundIps') {
  scope: rg
  params: {
    serverName: sql.outputs.serverName
    ipAddresses: api.outputs.possibleOutboundIps
  }
}

module budget 'modules/budget.bicep' = if (!empty(budgetAlertEmail)) {
  scope: rg
  params: {
    name: 'budget-evidencechain-${environmentName}'
    amount: budgetAmount
    contactEmail: budgetAlertEmail
    startDate: empty(budgetStartDate) ? budgetFallbackStartDate : budgetStartDate
  }
}

// azd stores these in .azure/<env>/.env; the scripts in infra/scripts read them.
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name
output SERVICE_API_NAME string = api.outputs.name
output SERVICE_API_URI string = api.outputs.uri
output API_PRINCIPAL_ID string = api.outputs.principalId
output SQL_SERVER_NAME string = sql.outputs.serverName
output SQL_SERVER_FQDN string = sql.outputs.serverFqdn
output SQL_DATABASE_NAME string = names.sqlDatabase
output KEY_VAULT_NAME string = keyVault.outputs.name
