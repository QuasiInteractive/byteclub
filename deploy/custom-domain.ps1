# Point byteclub.quasi-interactive.com at the Azure app, with a free managed HTTPS certificate.
#
# Run from the repo root:   .\deploy\custom-domain.ps1
# It tells you the two DNS records to add at GoDaddy, waits until they're live, then binds.

param(
    [string]$Domain = "byteclub.quasi-interactive.com",
    [string]$ResourceGroup = "byteclub-rg",
    [string]$App = "byteclub"
)
$ErrorActionPreference = "Stop"

$fqdn = az containerapp show -g $ResourceGroup -n $App --query properties.configuration.ingress.fqdn -o tsv
$verifyId = az containerapp show -g $ResourceGroup -n $App --query properties.customDomainVerificationId -o tsv
$sub = $Domain.Split(".")[0]

Write-Host ""
Write-Host "Add these two records in GoDaddy (quasi-interactive.com > DNS > Add New Record):" -ForegroundColor Cyan
Write-Host "  Type CNAME   Name $sub          Value $fqdn   TTL 1 hour"
Write-Host "  Type TXT     Name asuid.$sub    Value $verifyId   TTL 1 hour"
Write-Host ""
Read-Host "Press Enter once both are saved"

Write-Host "Waiting for DNS (usually a few minutes)..."
while ($true) {
    $cname = (Resolve-DnsName $Domain -Type CNAME -ErrorAction SilentlyContinue | Where-Object Type -eq "CNAME").NameHost
    $txt = (Resolve-DnsName "asuid.$Domain" -Type TXT -ErrorAction SilentlyContinue).Strings
    if ($cname -eq $fqdn -and $txt -contains $verifyId) { break }
    Start-Sleep -Seconds 20
}
Write-Host "DNS is live. Binding the domain and issuing the certificate (a few more minutes)..."

# The environment is shared and lives in another resource group, so pass its full id.
$envId = az containerapp show -g $ResourceGroup -n $App --query properties.managedEnvironmentId -o tsv
az containerapp hostname add -g $ResourceGroup -n $App --hostname $Domain --output none
az containerapp hostname bind -g $ResourceGroup -n $App --hostname $Domain `
    --environment $envId --validation-method CNAME --output none

Write-Host "Done: https://$Domain" -ForegroundColor Green
