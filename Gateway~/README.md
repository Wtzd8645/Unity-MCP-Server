# Gateway~

This directory is the Gateway component root.

- Role: MCP gateway process implemented with the official MCP C# SDK
- Source: package root (`Gateway~/`)
- Entry project: `UnityMcpGateway.csproj`
- Tool schemas: `schemas/`
- Default transport: MCP streamable HTTP via ASP.NET Core (`UNITY_MCP_STREAMABLE_HTTP_URL`, default `http://127.0.0.1:38100/mcp`)
- Default Unity Control transport: Named Pipe (`UNITY_MCP_CONTROL_TRANSPORT=pipe`)
