targetScope = 'resourceGroup'

@allowed(['dev', 'test'])
param environment string

@minLength(3)
@maxLength(20)
@description('Project prefix for resource names. Use lowercase letters, digits and hyphens; start and end with a letter or digit.')
param namePrefix string = 'webhookrouter'

@description('Optional ownership and billing tags, such as owner and costCenter. Reserved identification tags take precedence.')
param additionalTags object = {}

param location string = resourceGroup().location
@description('Static Web Apps has its own supported regions.')
param staticWebAppLocation string = 'eastasia'
param appServicePlanSku string = 'B1'
@allowed(['Free', 'Standard'])
param staticWebAppSku string = 'Free'
param sqlDatabaseName string = '${namePrefix}-${environment}-db'
param sqlDatabaseSku string = 'Basic'
param sqlDatabaseTier string = 'Basic'
@description('Object ID of the Microsoft Entra principal that will administer Azure SQL.')
@minLength(36)
@maxLength(36)
param sqlAdministratorObjectId string
param sqlAdministratorDisplayName string
@allowed(['Group', 'User'])
param sqlAdministratorPrincipalType string = 'Group'
@description('Public Google OAuth web client ID shared by the API and frontend.')
@minLength(1)
param googleClientId string
param additionalFrontendOrigins array = []
@description('Optional topics to provision in this namespace.')
param serviceBusTopics array = []
@description('Optional queues to provision in this namespace.')
param serviceBusQueues array = []

var suffix = uniqueString(subscription().subscriptionId, resourceGroup().id, environment)
var baseName = '${namePrefix}-${environment}-${suffix}'
var tags = union(additionalTags, {
  project: namePrefix
  application: 'WebhookRouter'
  environment: environment
  managedBy: 'Bicep'
})
var senderRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')

resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: '${baseName}-plan'
  location: location
  tags: union(tags, { component: 'hosting' })
  kind: 'linux'
  sku: { name: appServicePlanSku, capacity: 1 }
  properties: { reserved: true }
}

resource frontend 'Microsoft.Web/staticSites@2024-11-01' = {
  name: '${baseName}-web'
  location: staticWebAppLocation
  tags: union(tags, { component: 'frontend' })
  sku: { name: staticWebAppSku, tier: staticWebAppSku }
  properties: {
    provider: 'Custom'
    buildProperties: { skipGithubActionWorkflowGeneration: true }
  }
}

resource api 'Microsoft.Web/sites@2024-11-01' = {
  name: '${baseName}-api'
  location: location
  tags: union(tags, { component: 'backend' })
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      webSocketsEnabled: true
      healthCheckPath: '/healthz'
    }
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${baseName}-sql'
  location: location
  tags: union(tags, { component: 'database-server' })
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: sqlAdministratorPrincipalType
      login: sqlAdministratorDisplayName
      sid: sqlAdministratorObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: union(tags, { component: 'database' })
  sku: { name: sqlDatabaseSku, tier: sqlDatabaseTier }
  properties: { requestedBackupStorageRedundancy: 'Local' }
}

// Permit this App Service's possible outbound addresses, not all Azure services.
module sqlFirewall 'modules/sql-firewall.bicep' = {
  name: '${baseName}-sql-firewall'
  params: {
    serverName: sqlServer.name
    ruleNamePrefix: '${namePrefix}-${environment}'
    outboundIpAddresses: split(api.properties.possibleOutboundIpAddresses, ',')
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: '${baseName}-bus'
  location: location
  tags: union(tags, { component: 'messaging' })
  sku: { name: 'Standard', tier: 'Standard' }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource topics 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = [for topicName in serviceBusTopics: {
  parent: serviceBus
  name: topicName
  properties: { supportOrdering: true }
}]

resource queues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = [for queueName in serviceBusQueues: {
  parent: serviceBus
  name: queueName
  properties: { deadLetteringOnMessageExpiration: true }
}]

resource senderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, api.id, senderRoleId)
  scope: serviceBus
  properties: {
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: senderRoleId
  }
}

var frontendOrigins = concat(['https://${frontend.properties.defaultHostname}'], additionalFrontendOrigins)
resource apiSettings 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: api
  name: 'appsettings'
  properties: union({
    ASPNETCORE_ENVIRONMENT: 'Production'
    Authentication__Google__ClientId: googleClientId
    ConnectionStrings__DefaultConnection: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${database.name};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;'
  }, toObject(range(0, length(frontendOrigins)), i => 'Cors__AllowedOrigins__${i}', i => frontendOrigins[i]))
}

output apiUrl string = 'https://${api.properties.defaultHostName}'
output frontendUrl string = 'https://${frontend.properties.defaultHostname}'
output appServiceName string = api.name
output staticWebAppName string = frontend.name
output apiIdentityObjectId string = api.identity.principalId
output sqlServerHostName string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output serviceBusNamespace string = '${serviceBus.name}.servicebus.windows.net'
output resourceTags object = tags
output frontendBuildVariables object = {
  VITE_API_URL: 'https://${api.properties.defaultHostName}'
  VITE_GOOGLE_CLIENT_ID: googleClientId
  VITE_GOOGLE_REDIRECT_URI: 'https://${frontend.properties.defaultHostname}/'
}
