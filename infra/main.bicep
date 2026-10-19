// Byte Club on Azure: Container Apps (scale to zero) + Azure SQL Database free offer.
//
// No database password exists anywhere. The SQL server only accepts Microsoft Entra sign-in:
// Nick is the admin, and the app signs in with its own managed identity (granted access by
// deploy/azure.ps1 after this runs). Secrets (ntfy topic, bank details) arrive as secure
// parameters from the deploy script, which reads Nick's own user-secrets.

param location string = 'australiaeast'
param image string = 'ghcr.io/quasiinteractive/byteclub:latest'

@description('An existing Container Apps environment. Free subscriptions get one per region, so Byte Club shares the Clausewise one.')
param environmentId string

@description('Nick\'s Entra object id and sign-in name: the only SQL admin.')
param adminObjectId string
param adminLogin string

@secure()
param ntfyTopic string
@secure()
param bsb string = ''
@secure()
param accountNumber string = ''
@secure()
param accountName string = ''
@secure()
param payId string = ''

var name = 'byteclub'
var sqlServerName = '${name}-sql-${uniqueString(resourceGroup().id)}'

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true // no SQL logins, no passwords
      principalType: 'User'
      login: adminLogin
      sid: adminObjectId
      tenantId: subscription().tenantId
    }
  }
}

// Lets Azure services (the container app) reach the server; access still needs Entra sign-in.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// Free offer: serverless General Purpose, free monthly allowance, pauses instead of billing
// when the allowance runs out.
resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: name
  location: location
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      secrets: [
        { name: 'ntfy-topic', value: ntfyTopic }
        { name: 'bsb', value: empty(bsb) ? 'none' : bsb }
        { name: 'account-number', value: empty(accountNumber) ? 'none' : accountNumber }
        { name: 'account-name', value: empty(accountName) ? 'none' : accountName }
        { name: 'pay-id', value: empty(payId) ? 'none' : payId }
      ]
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'Database__Provider', value: 'SqlServer' }
            {
              name: 'ConnectionStrings__ByteClub'
              value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${name};Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60'
            }
            { name: 'Notify__NtfyTopic', secretRef: 'ntfy-topic' }
            { name: 'Business__Bsb', secretRef: 'bsb' }
            { name: 'Business__AccountNumber', secretRef: 'account-number' }
            { name: 'Business__AccountName', secretRef: 'account-name' }
            { name: 'Business__PayId', secretRef: 'pay-id' }
          ]
          probes: [
            { type: 'Readiness', httpGet: { path: '/healthz', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 10 }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1 // one replica: the sign-in code lives in memory
        rules: [ { name: 'http', http: { metadata: { concurrentRequests: '20' } } } ]
      }
    }
  }
}

output url string = 'https://${app.properties.configuration.ingress.fqdn}'
output sqlServer string = sql.properties.fullyQualifiedDomainName
output appIdentityName string = app.name
