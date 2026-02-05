from mcp.server.sse import TransportSecuritySettings
print(f"Fields: {TransportSecuritySettings.__fields__.keys() if hasattr(TransportSecuritySettings, '__fields__') else 'N/A'}")
print(f"Model fields: {TransportSecuritySettings.model_fields.keys() if hasattr(TransportSecuritySettings, 'model_fields') else 'N/A'}")
