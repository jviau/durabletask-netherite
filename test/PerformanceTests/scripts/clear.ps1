#!/usr/bin/pwsh

# read the settings that are common to all scripts
. ./settings.ps1

Write-Host "Clearing storage account..."
$connectionString = (az storage account show-connection-string --name $storageName --resource-group $groupName | ConvertFrom-Json).connectionString
$list = (az storage container list --account-name $storageName --output json --connection-string $connectionString | ConvertFrom-Json) 
$list | ForEach-Object -Process { Write-Host "Deleting container" $_.name ; az storage container delete --name $_.name --connection-string $connectionString }