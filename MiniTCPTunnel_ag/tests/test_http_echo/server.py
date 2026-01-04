from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, HTMLResponse
import uvicorn
import os

app = FastAPI()

# Simple In-Memory JSON-RPC Handler
@app.post("/rpc")
async def rpc_handler(request: Request):
    try:
        data = await request.json()
        
        # Validate JSON-RPC 2.0
        if data.get("jsonrpc") != "2.0":
            return JSONResponse({"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": None})
        
        method = data.get("method")
        params = data.get("params")
        req_id = data.get("id")
        
        # Implementation
        if method == "echo":
            # Echo logic
            text = params[0] if isinstance(params, list) and params else params.get("text")
            result = f"Echo: {text}"
            
            return {
                "jsonrpc": "2.0",
                "result": result,
                "id": req_id
            }
        else:
            return {
                "jsonrpc": "2.0",
                "error": {"code": -32601, "message": "Method not found"},
                "id": req_id
            }
            
    except Exception as e:
        return {
            "jsonrpc": "2.0",
            "error": {"code": -32603, "message": f"Internal error: {str(e)}"},
            "id": None
        }

@app.get("/", response_class=HTMLResponse)
async def serve_client():
    # Serve the index.html from the same folder
    try:
        with open("index.html", "r", encoding='utf-8') as f:
            return f.read()
    except FileNotFoundError:
        return "<h1>Client not found (index.html is missing)</h1>"

if __name__ == "__main__":
    # Run on port 8000
    uvicorn.run(app, host="0.0.0.0", port=8000)
