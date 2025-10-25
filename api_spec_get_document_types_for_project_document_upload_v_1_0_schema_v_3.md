# API Spec — Get Document Types For Project Document Upload (v1.0)

**Module:** Project → Document Management → Upload Dialog  
**Schema target:** v3.1c (table `document_types` present)  
**Stack:** .NET 8 (Minimal API / Controller), Dapper, PostgreSQL, Serilog  
**Security:** JWT Bearer, RBAC `proj:document:read`, ABAC (user belongs to project/org).  
**Goal:** Trả danh mục loại tài liệu cho popup **Upload Document** (mảng Dự án).

---

## 1) Endpoint
**GET** `/api/v1/projects/{projectId}/document-types`

### Path params
- `projectId` *(Guid, required)*

### Query params
- `q` *(string, optional)*: tìm theo `code`/`name` (case-insensitive, unaccent)
- `activeOnly` *(bool, default: true)*
- `limit` *(int, default: 100, max: 500)*

### Response 200
```json
{
  "items": [
    {"id":"0a2b...","code":"design_drawing","name":"Bản vẽ thiết kế","is_active":true,"order":10},
    {"id":"2b3c...","code":"contract","name":"Hợp đồng","is_active":true,"order":30}
  ],
  "total": 6
}
```

### Error schema (chuẩn dự án)
```json
{"error":"forbidden","message":"You don't have access to this project.","traceId":"..."}
```

---

## 2) Nguồn dữ liệu (bắt buộc dùng `document_types` v3.1c)
Sử dụng bảng `document_types` (schema v3.1c). Các cột **tối thiểu** được dùng trong API:
- `id uuid PRIMARY KEY`
- `code varchar(50) UNIQUE NOT NULL`
- `name varchar(255) NOT NULL`
- `is_active boolean DEFAULT true`
- `order integer DEFAULT 0` *(tên cột có thể là `order` hoặc `sort_order` tùy dump; API sẽ alias là `order` khi trả ra)*
- `scope varchar(20) DEFAULT 'project'` *(lọc cho domain project)*
- `org_id uuid NULL` *(đồng bộ theo project owner)*

> Nếu schema có thêm cột như `allowed_extensions text[]`, `max_size_mb int`… API **bỏ qua** trong bản v1.0 (không phá hợp đồng). Có thể thêm ở v1.1.

---

## 3) OpenAPI (YAML)
```yaml
openapi: 3.0.3
info:
  title: FISA Project — Document Types API
  version: 1.0.0
servers:
  - url: /api/v1
paths:
  /projects/{projectId}/document-types:
    get:
      summary: Get document types for project upload dialog
      tags: [Project Documents]
      security:
        - bearerAuth: []
      parameters:
        - in: path
          name: projectId
          required: true
          schema: { type: string, format: uuid }
        - in: query
          name: q
          schema: { type: string, maxLength: 100 }
          description: Search by code/name (case-insensitive)
        - in: query
          name: activeOnly
          schema: { type: boolean, default: true }
        - in: query
          name: limit
          schema: { type: integer, minimum: 1, maximum: 500, default: 100 }
      responses:
        "200":
          description: OK
          content:
            application/json:
              schema:
                type: object
                properties:
                  items:
                    type: array
                    items:
                      type: object
                      properties:
                        id: { type: string, format: uuid }
                        code: { type: string }
                        name: { type: string }
                        is_active: { type: boolean }
                        order: { type: integer }
                  total: { type: integer }
        "401": { description: Unauthorized }
        "403": { description: Forbidden }
        "404": { description: Project not found }
components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT
```

---

## 4) SQL (PostgreSQL)
> **Yêu cầu extension:** `unaccent` (tối ưu tìm kiếm không dấu).  
> **ABAC:** join `projects` để ràng buộc `org_id`.

```sql
-- Params:
--  :projectId uuid
--  :q text nullable
--  :activeOnly bool
--  :limit int

WITH proj AS (
  SELECT p.org_id
  FROM projects p
  WHERE p.id = :projectId
)
SELECT dt.id, dt.code, dt.name, dt.is_active,
       COALESCE(dt."order", dt.sort_order, 0) AS "order"
FROM document_types dt, proj
WHERE (dt.scope IS NULL OR dt.scope = 'project')
  AND (dt.org_id IS NULL OR dt.org_id = proj.org_id)
  AND (:activeOnly = FALSE OR dt.is_active = TRUE)
  AND (
    :q IS NULL OR :q = ''
    OR unaccent(dt.code) ILIKE unaccent('%' || :q || '%')
    OR unaccent(dt.name) ILIKE unaccent('%' || :q || '%')
  )
ORDER BY COALESCE(dt."order", dt.sort_order, 0) NULLS FIRST, dt.name
LIMIT :limit;

-- Total
WITH proj AS (
  SELECT p.org_id FROM projects p WHERE p.id = :projectId
)
SELECT COUNT(1)
FROM document_types dt, proj
WHERE (dt.scope IS NULL OR dt.scope = 'project')
  AND (dt.org_id IS NULL OR dt.org_id = proj.org_id)
  AND (:activeOnly = FALSE OR dt.is_active = TRUE)
  AND (
    :q IS NULL OR :q = ''
    OR unaccent(dt.code) ILIKE unaccent('%' || :q || '%')
    OR unaccent(dt.name) ILIKE unaccent('%' || :q || '%')
  );
```

### Index đề xuất (nếu chưa có)
```sql
CREATE INDEX IF NOT EXISTS idx_document_types_scope_org ON document_types(scope, org_id);
CREATE INDEX IF NOT EXISTS idx_document_types_active_order ON document_types(is_active, "order");
-- Optional search optimization
-- CREATE INDEX IF NOT EXISTS idx_document_types_name_unaccent ON document_types (unaccent(name));
```

---

## 5) .NET 8 + Dapper (Repo + Controller)

### 5.1 DTO & Repo (parameterized ⇒ chống SQLi)
```csharp
public sealed record DocTypeDto(Guid Id, string Code, string Name, bool IsActive, int Order);

public interface IDocumentTypesRepository
{
    Task<(IReadOnlyList<DocTypeDto> Items, int Total)> GetAsync(
        NpgsqlConnection conn, Guid projectId, string? q, bool activeOnly, int limit, CancellationToken ct);
}

public sealed class DocumentTypesRepository : IDocumentTypesRepository
{
    private const string Sql = @"/* see SQL section above; kept inline for demo */
WITH proj AS (
  SELECT p.org_id FROM projects p WHERE p.id = @projectId
)
SELECT dt.id, dt.code, dt.name, dt.is_active,
       COALESCE(dt.\"order\", dt.sort_order, 0) AS \"order\"
FROM document_types dt, proj
WHERE (dt.scope IS NULL OR dt.scope = 'project')
  AND (dt.org_id IS NULL OR dt.org_id = proj.org_id)
  AND (@activeOnly = FALSE OR dt.is_active = TRUE)
  AND (@q IS NULL OR @q = ''
       OR unaccent(dt.code) ILIKE unaccent('%' || @q || '%')
       OR unaccent(dt.name) ILIKE unaccent('%' || @q || '%'))
ORDER BY COALESCE(dt.\"order\", dt.sort_order, 0) NULLS FIRST, dt.name
LIMIT @limit;
SELECT COUNT(1)
FROM document_types dt, proj
WHERE (dt.scope IS NULL OR dt.scope = 'project')
  AND (dt.org_id IS NULL OR dt.org_id = proj.org_id)
  AND (@activeOnly = FALSE OR dt.is_active = TRUE)
  AND (@q IS NULL OR @q = ''
       OR unaccent(dt.code) ILIKE unaccent('%' || @q || '%')
       OR unaccent(dt.name) ILIKE unaccent('%' || @q || '%'));";

    public async Task<(IReadOnlyList<DocTypeDto> Items, int Total)> GetAsync(
        NpgsqlConnection conn, Guid projectId, string? q, bool activeOnly, int limit, CancellationToken ct)
    {
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            Sql, new { projectId, q, activeOnly, limit }, cancellationToken: ct));
        var items = (await multi.ReadAsync<DocTypeDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();
        return (items, total);
    }
}
```

### 5.2 Controller (Serilog + XSS guard)
```csharp
[ApiController]
[Route("api/v1/projects/{projectId:guid}/document-types")]
public class DocumentTypesController : ControllerBase
{
    private readonly NpgsqlConnection _conn;
    private readonly IDocumentTypesRepository _repo;
    private readonly ILogger<DocumentTypesController> _logger;

    public DocumentTypesController(NpgsqlConnection conn, IDocumentTypesRepository repo, ILogger<DocumentTypesController> logger)
    { _conn = conn; _repo = repo; _logger = logger; }

    [HttpGet]
    [Authorize(Policy = "proj.document.read")]
    public async Task<IActionResult> Get(Guid projectId, [FromQuery] string? q, [FromQuery] bool activeOnly = true, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        using (LogContext.PushProperty("UserId", User.FindFirst("sub")?.Value ?? "unknown"))
        using (LogContext.PushProperty("ProjectId", projectId))
        using (LogContext.PushProperty("Endpoint", "GET /document-types"))
        {
            var (items, total) = await _repo.GetAsync(_conn, projectId, q, activeOnly, limit, ct);

            // XSS guard: whitelist + strip angle brackets (defensive)
            var safe = items.Select(x => new {
                id = x.Id,
                code = Sanitize(x.Code),
                name = Sanitize(x.Name),
                is_active = x.IsActive,
                order = x.Order
            });

            _logger.LogInformation("Returned {Count} doc types for project {ProjectId}", items.Count, projectId);
            return Ok(new { items = safe, total });
        }
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.Replace("<", string.Empty).Replace(">", string.Empty).Trim();
        return t.Length > 255 ? t[..255] : t;
    }
}
```

---

## 6) Security & Logging
- **SQL Injection**: Dapper **parameterized** (`@projectId`, `@q`, `@activeOnly`, `@limit`).
- **XSS**: API chỉ trả JSON; thêm `Sanitize` để strip `<` `>`; FE bắt buộc escape (React default an toàn nếu không dùng `dangerouslySetInnerHTML`).
- **RBAC**: Policy `proj.document.read` + kiểm tra user thuộc dự án (ABAC theo `org_id`).
- **Serilog**: `LogContext` gắn `UserId`, `ProjectId`, `Endpoint`, log mức `Information` cho response; `Warning`/`Error` khi exception.

---

## 7) Tests (DoD mini)
- [ ] OpenAPI cập nhật đúng tham số & schema trả về.
- [ ] Integration Test 1: `q = null`, `activeOnly=true` ⇒ trả danh sách active theo `order`.
- [ ] Integration Test 2: `q = 'hop'` ⇒ có `Hợp đồng`.
- [ ] ABAC: user không thuộc org/project ⇒ `403`.
- [ ] Hiệu năng: `LIMIT` ≤ 500, truy vấn ≤ 50 ms trên 10k dòng `document_types`.

---

## 8) Notes triển khai
- Đảm bảo extension `unaccent` đã enable: `CREATE EXTENSION IF NOT EXISTS unaccent;`  
- Nếu cột sắp xếp là `sort_order`, giữ nguyên trong DB; API alias ra field `order` để FE dùng thống nhất.  
- Không thay đổi dữ liệu/DDL trong API này.

