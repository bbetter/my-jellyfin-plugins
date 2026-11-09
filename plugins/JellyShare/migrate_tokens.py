import json
import sys
import uuid

try:
    data = json.load(sys.stdin)
    result = {}
    for entry in data:
        token = entry.get("token") or entry.get("Token")
        item_id_raw = entry.get("itemId") or entry.get("ItemId")
        expires_at = entry.get("expiresAt") or entry.get("ExpiresAt")
        
        if token and item_id_raw and expires_at:
            # Normalize ItemId to hyphenated GUID
            try:
                item_id = str(uuid.UUID(item_id_raw))
                result[token] = {
                    "ItemId": item_id,
                    "ExpiresAt": expires_at
                }
            except ValueError:
                print(f"Warning: Invalid UUID {item_id_raw}", file=sys.stderr)
                
    print(json.dumps(result, indent=2))
except Exception as e:
    print(f"Error: {e}", file=sys.stderr)
    sys.exit(1)
