# Document API

This repository hosts a .NET 8 Web API that exposes the **Get Document Types for Project Document Upload** endpoint described in `api_spec_get_document_types_for_project_document_upload_v_1_0_schema_v_3.md`. The service authenticates requests with JWT bearer tokens, enforces the `proj:document:read` permission together with project/org attribute checks, and queries PostgreSQL for project-scoped document type metadata using Dapper.

## Features

- **Endpoint:** `GET /api/v1/projects/{projectId}/document-types` with optional `q`, `activeOnly`, and `limit` parameters.
- **Security:** Serilog logging, JWT bearer authentication, and RBAC + ABAC enforcement through `ProjectAccessEvaluator`.
- **Data access:** PostgreSQL connection managed via `NpgsqlConnection` and the `DocumentTypesRepository`, which executes the SQL defined in the specification to return paginated document type listings.
- **Service layer:** `DocumentTypesService` centralises project lookups, access evaluation, sanitisation, and repository aggregation so controllers remain thin HTTP adapters.
- **Tooling:** Swagger UI for local exploration, strongly typed logging, and environment-based configuration in `appsettings*.json`.

## Getting Started

1. Ensure PostgreSQL is available and contains the `projects` and `document_types` tables from schema v3.1c with the `unaccent` extension enabled.
2. Set the `ConnectionStrings:Default` entry in `appsettings.Development.json` (or environment variables) to point to your database.
3. Configure `Authentication:Authority`, `Authentication:Audience`, and `Authentication:RequireHttpsMetadata` to match your identity provider.
4. Restore and build the solution:

   ```bash
   dotnet restore
   dotnet build
   ```

5. Run the API locally:

   ```bash
   dotnet run --project DocumentApi
   ```

6. Use the included `DocumentApi.http` file or Swagger UI to test the endpoint.

## Testing the Endpoint

Send an authenticated request with a bearer token that contains either the project identifier (`project_id`, `projects`, etc.) or an organisation claim matching the project owner. A successful response returns:

```json
{
  "items": [
    {
      "id": "uuid",
      "code": "contract",
      "name": "Hợp đồng",
      "is_active": true,
      "order": 10
    }
  ],
  "total": 1
}
```

Errors follow the project-standard schema: `{ "error": "forbidden", "message": "...", "traceId": "..." }`.
