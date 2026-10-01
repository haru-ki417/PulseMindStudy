// Pulse & Mind Study の Azure 環境（App Service + Azure SQL Database）
//
// ・Web アプリは Linux の App Service（.NET 10）。Blazor Server のため WebSocket を有効にする
// ・データベースは Azure SQL Database。可能なら無料枠（サーバーレス、月 10 万 vCore 秒まで）を使う
// ・アプリからデータベースへは「マネージド ID」で接続し、パスワードをどこにも置かない
// ・データベースの管理者も Microsoft Entra ID のユーザーだけにする（SQL のパスワード認証を使わない）
//
// 使い方は docs/deploy.md を参照。

@description('リソースを置く場所。データを国内に置くため東日本を既定にする')
param location string = 'japaneast'

@description('名前の頭に付ける文字（英小文字と数字、3〜12 文字）')
@minLength(3)
@maxLength(12)
param prefix string = 'pulsemind'

@description('データベース管理者にする Entra ID ユーザーのログイン名（例: you@example.onmicrosoft.com）')
param sqlAdminLogin string

@description('データベース管理者にする Entra ID ユーザーのオブジェクト ID（az ad signed-in-user show --query id -o tsv）')
param sqlAdminObjectId string

@description('App Service プランの価格レベル（B1 以上を推奨。Blazor Server は常時接続が必要なため F1 は不向き）')
param appServiceSku string = 'B1'

@description('Azure SQL Database の無料枠を使う（1 つのサブスクリプションで使える無料データベースは限られる）')
param useFreeSqlOffer bool = true

@description('確認メールを送る SMTP サーバー（空なら送らない。その場合はメール確認なしで登録できる）')
param smtpHost string = ''

@description('SMTP の差出人アドレス')
param smtpFrom string = ''

@description('SMTP のユーザー名')
param smtpUserName string = ''

@secure()
@description('SMTP のパスワード')
param smtpPassword string = ''

var suffix = uniqueString(resourceGroup().id)
var appName = '${prefix}-${suffix}'
var sqlServerName = '${prefix}-sql-${suffix}'
var databaseName = 'PulseMindStudy'
var useSmtp = !empty(smtpHost) && !empty(smtpFrom)

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-plan-${suffix}'
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true // Linux
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'User'
    }
  }
}

// Azure のサービス（App Service）からの接続だけを許可する
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: useFreeSqlOffer
    ? {
        name: 'GP_S_Gen5'
        tier: 'GeneralPurpose'
        family: 'Gen5'
        capacity: 2
      }
    : {
        name: 'Basic'
        tier: 'Basic'
      }
  properties: useFreeSqlOffer
    ? {
        useFreeLimit: true
        freeLimitExhaustionBehavior: 'AutoPause' // 無料枠を使い切ったら、課金せずに月末まで一時停止する
        autoPauseDelay: 60
        minCapacity: json('0.5')
        requestedBackupStorageRedundancy: 'Local'
      }
    : {
        requestedBackupStorageRedundancy: 'Local'
      }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true // Blazor Server は同じサーバーにつなぎ続ける必要がある
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      webSocketsEnabled: true
      alwaysOn: appServiceSku != 'F1'
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz' // データベースに触れない確認（データベースが休止できるように）
      appSettings: concat([
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Database__MigrateOnStartup', value: 'true' }
        // 利用者の IP アドレスは App Service の既定（ASPNETCORE_FORWARDEDHEADERS_ENABLED）で受け取る。アプリ側で二重に処理しない
        { name: 'Identity__RequireConfirmedAccount', value: string(useSmtp) }
      ], useSmtp ? [
        { name: 'Smtp__Host', value: smtpHost }
        { name: 'Smtp__From', value: smtpFrom }
        { name: 'Smtp__UserName', value: smtpUserName }
        { name: 'Smtp__Password', value: smtpPassword }
      ] : [])
      connectionStrings: [
        {
          name: 'DefaultConnection'
          type: 'SQLAzure'
          // パスワードは含まない。アプリのマネージド ID で接続する
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
      ]
    }
  }
}

// 基本認証（FTP / Web デプロイのパスワード）を無効にし、Entra ID での配置だけにする
resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: {
    allow: false
  }
}

output appName string = app.name
output appUrl string = 'https://${app.properties.defaultHostName}'
output sqlServerName string = sqlServer.name
output databaseName string = databaseName

@description('データベースで一度だけ実行する SQL（アプリのマネージド ID に権限を与える）')
output grantSql string = 'CREATE USER [${app.name}] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [${app.name}]; ALTER ROLE db_datawriter ADD MEMBER [${app.name}]; ALTER ROLE db_ddladmin ADD MEMBER [${app.name}];'
