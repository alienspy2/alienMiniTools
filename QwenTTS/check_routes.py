from mcp.server.fastmcp import FastMCP
mcp = FastMCP("test")
app = mcp.sse_app()
for route in app.routes:
    print(f"Path: {route.path}, Name: {getattr(route, 'name', 'N/A')}")
