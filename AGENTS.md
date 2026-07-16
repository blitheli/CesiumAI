# AGENTS.md

## Cursor Cloud specific instructions

CesiumAI is a two-tier app: an ASP.NET Core (.NET 10) backend API and a React + Cesium (Vite) frontend, connected by `POST /api/chat`. Standard setup, run, test, and deploy commands live in `README.md` and `frontend/package.json`; prefer those. Notes below are the non-obvious caveats for running in this environment.

### Toolchain / environment
- `.NET 10 SDK` is installed at `/usr/local/dotnet` and symlinked to `/usr/local/bin/dotnet` (already on `PATH`). Node.js 22 and npm are preinstalled. These are baked into the VM snapshot; the startup update script only refreshes npm deps + Playwright Chromium.
- `backend/skills/` is required at backend startup and is `.gitignore`d (not committed). It is populated from the external `gitee.com/blitheli/astrox-skills` repo (its `skills/` contents) and persists in the VM snapshot. If it is ever missing, the backend fails fast at startup; repopulate with: `git clone --depth 1 https://gitee.com/blitheli/astrox-skills.git /tmp/astrox-skills && mkdir -p backend/skills && cp -R /tmp/astrox-skills/skills/. backend/skills/`.

### Running the backend (`http://localhost:5088`)
- `dotnet run --project backend/CesiumAI.Api` (run from repo root).
- Startup uses `ValidateOnStart`: it fails immediately unless `Agent:ApiKey` is a non-empty value, `Agent:Endpoint`/`Astrox:BaseUrl` are absolute HTTP(S) URLs, and `backend/skills/` exists. Provide the key via env `Agent__ApiKey=...` (or User Secrets). A placeholder key is enough to boot and serve `/healthz` (returns `Healthy`); it is NOT enough for real chat.
- `/healthz` and startup do NOT call the LLM or Astrox. Only actual `POST /api/chat` requests hit the external OpenAI-compatible LLM (default `api.moonshot.cn`) and Astrox. A live chat therefore needs a real `Agent:ApiKey` secret; without it, `/api/chat` returns HTTP 500 (`HTTP 401 invalid_authentication_error`) — the rest of the pipeline is verified working.

### Running the frontend (`http://localhost:5173`)
- `cd frontend && npm run dev`. Set `VITE_API_BASE_URL=http://localhost:5088` so the browser calls the backend cross-origin (backend has a dev CORS policy for `http://localhost:5173`). Without it the app requests same-origin `/api/chat`.

### Testing (no external services required)
- Backend: `dotnet test CesiumAI.slnx`.
- Frontend: `npm test -- --run` (unit), `npm run lint` (oxlint), `npm run typecheck`, `npm run build`, `npm run e2e` (Playwright). The e2e suite starts its own Vite server on `:5173` and mocks `POST /api/chat`, so it needs neither the backend, the LLM, nor Astrox. `npm run e2e` takes ~2 minutes.
