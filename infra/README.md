# Azure deployment (optional)

The evaluated deliverable is `docker compose up --build`. This folder deploys the same API to Azure:
App Service (Linux B1, .NET 10) → Azure SQL (S1, Entra-only auth) with Key Vault, Log Analytics and Application Insights, in one resource group per azd environment. The SPA is deployed separately on Vercel.

Approximate cost while running: **≈ $45/month** at US list prices (B1 ≈ $12.41, S1 ≈ $29.43, the rest ≈ $1). `azd down --purge` removes everything.

## Prerequisites

`az login`, `azd auth login`, the .NET 10 SDK and [go-sqlcmd](https://learn.microsoft.com/sql/tools/sqlcmd/go-sqlcmd-utility). Your account becomes the SQL Entra admin and a Key Vault Secrets Officer.

## Steps

1. **Create the environment** (local only, stored in `.azure/<env>/.env`, git-ignored):

   ```bash
   azd env new demo --location centralus
   azd env set SQL_ADMIN_LOGIN "$(az ad signed-in-user show --query userPrincipalName -o tsv)"
   azd env set BUDGET_ALERT_EMAIL you@example.com        # optional cost alert
   azd env set BUDGET_START_DATE 2026-10-01T00:00:00Z    # first day of the current month, pinned
   ```

2. **Provision:** `azd provision`. Use it rather than `azd up`, because the database must be prepared before the code is deployed.

3. **Set the secrets.** They're generated in memory and never written to disk:

   ```bash
   kv="$(azd env get-value KEY_VAULT_NAME)"
   az keyvault secret set --vault-name "$kv" -n Jwt--SigningKey --value "$(openssl rand -base64 48)" -o none
   az keyvault secret set --vault-name "$kv" -n Integrity--Keys--k1 --value "$(openssl rand -base64 32)" -o none
   ```

   App Service tried these references during provisioning, before the secrets existed, and cached the failure. Ask it to fetch them again:

   ```bash
   az rest --method post --url "https://management.azure.com$(az webapp show -g "$(azd env get-value AZURE_RESOURCE_GROUP)" -n "$(azd env get-value SERVICE_API_NAME)" --query id -o tsv)/config/configreferences/appsettings/refresh?api-version=2025-03-01"
   ```

4. **Prepare the database:** `infra/scripts/prepare-database.sh`. It opens a temporary firewall rule for your IP, then:
   - applies the migrations;
   - creates the API's managed-identity user: it can read, write custody by column, and never update or delete custody events (`DENY UPDATE, DELETE`);
   - allows SNAPSHOT reads and sets compatibility level 170.

   The firewall rule is removed when the script ends.

5. **Load the dataset.** The seeder signs the chains with the Key Vault key, which it receives through the environment only. Open a firewall rule for its run:

   ```bash
   rg="$(azd env get-value AZURE_RESOURCE_GROUP)"; sql="$(azd env get-value SQL_SERVER_NAME)"; kv="$(azd env get-value KEY_VAULT_NAME)"
   ip="$(curl -fsS https://api.ipify.org)"
   az sql server firewall-rule create -g "$rg" -s "$sql" -n laptop --start-ip-address "$ip" --end-ip-address "$ip" -o none
   Integrity__ActiveKeyId=k1 \
   Integrity__Keys__k1="$(az keyvault secret show --vault-name "$kv" -n Integrity--Keys--k1 --query value -o tsv)" \
     dotnet run --project backend/tools/EvidenceChain.Seeder -c Release -- seed --reset --seed 42 \
     --connection "Server=tcp:$(azd env get-value SQL_SERVER_FQDN),1433;Database=$(azd env get-value SQL_DATABASE_NAME);Authentication=Active Directory Default;Encrypt=True;"
   az sql server firewall-rule delete -g "$rg" -s "$sql" -n laptop -o none
   ```

   `--reset` replaces everything in the database. The seed is anchored to today, so seed within a day of a demo: only the overdue fixture is then overdue.

6. **Deploy the API:** `azd deploy api`. Check `$(azd env get-value SERVICE_API_URI)/api/v1/health/live`, then `/api/v1/evidence` with a token.

7. **Deploy the SPA on Vercel.** Import the repository and leave Root Directory at the repository root; the root `vercel.json` builds `frontend/`.
   - Set `VITE_API_BASE_URL` to the `SERVICE_API_URI` value.
   - Set the Node.js version to 24.x (Project Settings → Build and Deployment).

   Then allow its origin and re-provision, which only updates the app settings:

   ```bash
   azd env set CORS_ORIGIN https://<project>.vercel.app
   azd provision
   ```

## Teardown

`azd down --purge` deletes the resource group and purges the Key Vault, so its name can be reused at once.

## Parameters (azd environment variables)

| Variable | Default | Meaning |
|---|---|---|
| `SQL_ADMIN_LOGIN` | required | Display label of the SQL Entra admin (your UPN). |
| `CORS_ORIGIN` | empty | Vercel production origin. |
| `APP_SERVICE_SKU` | `B1` | `B2` in the production design. |
| `SQL_DATABASE_SKU` | `S1` | `S2` in the production design. |
| `SQL_NETWORK_ACCESS` | `AppOutboundIps` | `AllAzureServices` opens SQL to every Azure IP instead. |
| `KEY_VAULT_PURGE_PROTECTION` | `Disabled` | `Enabled` for production. It's irreversible, and keeps a deleted vault's name reserved for 7 days. |
| `BUDGET_ALERT_EMAIL` | empty | Enables a $60 monthly budget (`budgetAmount` in `main.bicep`) with 80% actual and 100% forecast alerts. |

## Troubleshooting

- **The custody migration stops with "Evidence holds tracer rows".** The database holds rows from before the custody schema. Empty `dbo.Evidence`, run step 4 again, then step 5.

- **`ProvisioningDisabled: Provisioning is restricted in this region`** for the SQL server. Some subscriptions can't create Azure SQL servers in busy regions; on this project's subscription, East US 2, East US and North Central US were blocked. Pick a region that allows it (this deployment uses Central US), then run `azd down --purge`, `azd env set AZURE_LOCATION <region>` and `azd provision`.
- **`CREATE USER … FROM EXTERNAL PROVIDER` fails in step 4.** The SQL admin must be able to read the directory. A guest or Microsoft-account admin needs a directory role (for example, the subscription creator's Global Administrator).
- **The API can't reach SQL after the plan was scaled or moved.** App Service outbound IPs changed. Delete the old rules and re-provision:

  ```bash
  rg="$(azd env get-value AZURE_RESOURCE_GROUP)"; sql="$(azd env get-value SQL_SERVER_NAME)"
  az sql server firewall-rule list -g "$rg" -s "$sql" --query "[?starts_with(name, 'app-outbound-')].name" -o tsv \
    | xargs -I{} az sql server firewall-rule delete -g "$rg" -s "$sql" -n {}
  azd provision
  ```
