// Key Vault in RBAC mode; the deploying person can set and read secrets.
param location string
param tags object
param name string
param purgeProtection bool
param officerPrincipalId string
param officerPrincipalType string

var keyVaultSecretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource vault 'Microsoft.KeyVault/vaults@2026-02-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    // ARM rejects false here; leaving it out is how "disabled" is expressed.
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}

resource officer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, officerPrincipalId, keyVaultSecretsOfficer)
  properties: {
    principalId: officerPrincipalId
    principalType: officerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficer)
  }
}

output name string = vault.name
