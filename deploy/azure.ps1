# Deploy Byte Club to Azure: Container Apps + Azure SQL (free offer), passwordless.
#
# Run it yourself from the repo root, in Windows PowerShell or pwsh:   .\deploy\azure.ps1
#
# It reads your ntfy topic and bank details from YOUR user-secrets (dotnet user-secrets) and
# hands them straight to Azure as hidden values. Nothing is printed and nothing is committed.

param(
    [string]$ResourceGroup = "byteclub-rg",
    [string]$Location = "australiaeast",
    [string]$Image = "ghcr.io/quasiinteractive/byteclub:latest"
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

# --- your secrets, from user-secrets ---------------------------------------------------------
$secrets = @{}
dotnet user-secrets list --project api | ForEach-Object {
    if ($_ -match '^\s*([^=]+?)\s*=\s*(.*)$') { $secrets[$Matches[1]] = $Matches[2] }
}
if (-not $secrets['Notify:NtfyTopic']) { throw "Notify:NtfyTopic isn't in your user-secrets. Add it first." }
function Secret($key) { if ($secrets[$key]) { $secrets[$key] } else { "" } }

# --- one-time: switch on the services --------------------------------------------------------
Write-Host "Making sure Azure SQL and Container Apps are enabled (first time takes a minute)..."
az provider register --namespace Microsoft.Sql --wait
az provider register --namespace Microsoft.App --wait

# --- you are the only database admin -----------------------------------------------------------
$me = az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" -o json | ConvertFrom-Json

Write-Host "Deploying (about 5 minutes)..."
az group create --name $ResourceGroup --location $Location --output none
$out = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file infra/main.bicep `
    --parameters image=$Image adminObjectId=$($me.id) adminLogin=$($me.upn) `
        ntfyTopic=$(Secret 'Notify:NtfyTopic') bsb=$(Secret 'Business:Bsb') `
        accountNumber=$(Secret 'Business:AccountNumber') accountName=$(Secret 'Business:AccountName') `
        payId=$(Secret 'Business:PayId') `
    --query properties.outputs -o json | ConvertFrom-Json
$sqlServer = $out.sqlServer.value
$appName = $out.appIdentityName.value

# --- let the app's identity into the database (needs your sign-in, once) -----------------------
Write-Host "Giving the app access to its database..."
$myIp = (Invoke-RestMethod -Uri "https://api.ipify.org")
$serverShort = $sqlServer.Split('.')[0]
az sql server firewall-rule create -g $ResourceGroup -s $serverShort -n "deploy-temp" `
    --start-ip-address $myIp --end-ip-address $myIp --output none
try {
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    $sqlText = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$appName')
    CREATE USER [$appName] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$appName];
ALTER ROLE db_datawriter ADD MEMBER [$appName];
ALTER ROLE db_ddladmin ADD MEMBER [$appName];
"@
    # Windows PowerShell's built-in SqlClient accepts an Entra access token: no password involved.
    $grant = {
        param($server, $token, $sqlText)
        $conn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$server,1433;Database=byteclub;Encrypt=True;Connect Timeout=90")
        $conn.AccessToken = $token
        $conn.Open()
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sqlText; [void]$cmd.ExecuteNonQuery()
        $conn.Close()
    }
    powershell.exe -NoProfile -Command $grant -args $sqlServer, $token, $sqlText
}
finally {
    az sql server firewall-rule delete -g $ResourceGroup -s $serverShort -n "deploy-temp" --output none
}

# --- restart so the app starts with database access ------------------------------------------
$rev = az containerapp revision list -g $ResourceGroup -n $appName --query "[0].name" -o tsv
az containerapp revision restart -g $ResourceGroup -n $appName --revision $rev --output none

Write-Host "Byte Club is live at $($out.url.value)" -ForegroundColor Green
Write-Host "Next: the short address. Tell Claude 'deployed' and it'll set up byteclub.quasi-interactive.com."
