# Azure への公開手順

Pulse & Mind Study を Azure（App Service + Azure SQL Database）に公開する手順です。
パスワードを使わない構成（マネージド ID と OIDC）にしているため、手順の中で秘密の値を保存する場面はほとんどありません。

```
GitHub Actions ──(OIDC)──> Azure App Service (Linux, .NET 10) ──(マネージド ID)──> Azure SQL Database
                                     ↑ HTTPS
                         ブラウザ / IoTBridge（機器トークン）
```

## 0. 必要なもの

- Azure のサブスクリプション（学生なら Azure for Students が使えます）
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)（`az --version` で確認）
- このリポジトリを自分の GitHub に置いたもの

## 1. リソースを作る

```powershell
az login
az group create --name rg-pulsemind --location japaneast

# データベース管理者にする自分のアカウント
$login = az ad signed-in-user show --query userPrincipalName -o tsv
$oid   = az ad signed-in-user show --query id -o tsv

az deployment group create `
  --resource-group rg-pulsemind `
  --template-file infra/main.bicep `
  --parameters sqlAdminLogin=$login sqlAdminObjectId=$oid
```

終わると `appName`・`appUrl`・`grantSql` が表示されます（あとで使います）。

- 既定では Azure SQL Database の **無料枠**（サーバーレス）を使います。すでに無料枠を使っている場合は `useFreeSqlOffer=false` を付けてください（Basic 料金になります）。
- App Service は B1（月 2,000 円前後）です。使わない間は `az webapp stop` で止められます（プラン料金はかかります）。

## 2. アプリにデータベースの権限を与える（最初の1回だけ）

アプリはパスワードではなく **マネージド ID** でデータベースに接続します。その ID をデータベースのユーザーとして登録します。

1. Azure portal でデータベース「PulseMindStudy」を開き、**クエリ エディター** を選ぶ
2. 「Microsoft Entra 認証」で自分のアカウントとしてログインする（自分の IP の許可を求められたら許可する）
3. 手順 1 で表示された `grantSql` の内容を貼り付けて実行する

```sql
CREATE USER [pulsemind-xxxx] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [pulsemind-xxxx];
ALTER ROLE db_datawriter ADD MEMBER [pulsemind-xxxx];
ALTER ROLE db_ddladmin  ADD MEMBER [pulsemind-xxxx];  -- 起動時にテーブルを作る・更新するため
```

## 3. GitHub Actions から配置できるようにする（OIDC）

```powershell
$sub = az account show --query id -o tsv
$tenant = az account show --query tenantId -o tsv
$app = az ad app create --display-name "github-pulsemind-deploy" --query appId -o tsv
az ad sp create --id $app
az role assignment create --assignee $app --role "Website Contributor" `
  --scope "/subscriptions/$sub/resourceGroups/rg-pulsemind"

# GitHub の「production」環境からの実行だけを信頼する（<GitHubユーザー名>/<リポジトリ名> を置き換える）
az ad app federated-credential create --id $app --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<GitHubユーザー名>/<リポジトリ名>:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

GitHub のリポジトリで **Settings → Environments** に `production` を作り、**Settings → Secrets and variables → Actions → Variables** に次を登録します（どれも秘密の値ではありません）。

| 名前 | 値 |
|---|---|
| `AZURE_CLIENT_ID` | 上の `$app` |
| `AZURE_TENANT_ID` | 上の `$tenant` |
| `AZURE_SUBSCRIPTION_ID` | 上の `$sub` |
| `AZURE_WEBAPP_NAME` | 手順 1 の `appName` |

## 4. 配置する

GitHub の **Actions → Deploy → Run workflow**。テスト → 発行 → 配置 → `/healthz` の確認まで自動で行います。
初回の起動時に、アプリがデータベースのテーブルを作ります。

## 5. （任意）確認メールを送る

SMTP サーバー（例: Azure Communication Services のメール、SendGrid、Gmail のアプリパスワード）がある場合、
手順 1 のコマンドに次を付けて再実行すると、登録時にメールアドレスの確認が必須になります。

```powershell
--parameters smtpHost=smtp.example.com smtpFrom=noreply@example.com smtpUserName=... smtpPassword=...
```

## 困ったとき

- **起動しない / 500 エラー**: Azure portal の App Service →「ログ ストリーム」でエラーを確認します。
  `Login failed for user '<token-identified principal>'` と出る場合は、手順 2 が済んでいません。
- **しばらく使わないと最初の表示が遅い**: 無料枠のデータベースは使われていないと自動で停止し、次の接続で起動します（数十秒）。
- **ログインし直しになる**: Cookie の暗号化の鍵はデータベースに保存しているので、再起動では起きません。データベースを作り直した場合は全員が再ログインになります。
