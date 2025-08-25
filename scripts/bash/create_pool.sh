#!/bin/bash

# Default values
BASE_URL="http://localhost:8080"
TOKEN_A_DECIMALS=9
TOKEN_B_DECIMALS=6
RATIO="1:2"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --url)
            BASE_URL="$2"
            shift 2
            ;;
        --token-a-decimals)
            TOKEN_A_DECIMALS="$2"
            shift 2
            ;;
        --token-b-decimals)
            TOKEN_B_DECIMALS="$2"
            shift 2
            ;;
        --ratio)
            RATIO="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Create a new trading pool with specified parameters"
            echo ""
            echo "Options:"
            echo "  --url URL                 Base URL (default: http://localhost:8080)"
            echo "  --token-a-decimals NUM    Token A decimals 0-9 (default: 9)"
            echo "  --token-b-decimals NUM    Token B decimals 0-9 (default: 6)"
            echo "  --ratio RATIO            Ratio in X:Y format, one side must be 1 (default: 1:2)"
            echo "  -h, --help               Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0                                          # Create pool with defaults (9:6 decimals, 1:2 ratio)"
            echo "  $0 --token-a-decimals 3 --token-b-decimals 0 --ratio 1:2"
            echo "  $0 --ratio 10:1                           # 10 Token A = 1 Token B"
            echo "  $0 --ratio 1:160                          # 1 Token A = 160 Token B"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Validate parameters
if [[ ! "$TOKEN_A_DECIMALS" =~ ^[0-9]$ ]]; then
    echo "❌ Error: Token A decimals must be between 0 and 9"
    exit 1
fi

if [[ ! "$TOKEN_B_DECIMALS" =~ ^[0-9]$ ]]; then
    echo "❌ Error: Token B decimals must be between 0 and 9"
    exit 1
fi

# Validate ratio format
if [[ ! "$RATIO" =~ ^[0-9]+:[0-9]+$ ]]; then
    echo "❌ Error: Ratio must be in format 'X:Y' where X and Y are whole numbers (e.g., '1:2', '10:1')"
    exit 1
fi

# Extract ratio parts
IFS=':' read -r left right <<< "$RATIO"

# Validate that exactly one side equals 1
if [[ "$left" -ne 1 && "$right" -ne 1 ]] || [[ "$left" -eq 1 && "$right" -eq 1 ]]; then
    echo "❌ Error: Ratio must have exactly one side equal to 1 (e.g., '1:10' or '10:1')"
    exit 1
fi

# Generate unique ID
REQUEST_ID=$(uuidgen 2>/dev/null || echo "$(date +%s)-$$")

# Build JSON payload
JSON_PAYLOAD=$(cat <<EOF
{
    "jsonrpc": "2.0",
    "method": "create_pool",
    "id": "$REQUEST_ID",
    "params": {
        "token_a_decimals": $TOKEN_A_DECIMALS,
        "token_b_decimals": $TOKEN_B_DECIMALS,
        "ratio": "$RATIO"
    }
}
EOF
)

URL="$BASE_URL/api/jsonrpc"

echo "🔧 Creating pool with parameters:"
echo "   Token A Decimals: $TOKEN_A_DECIMALS"
echo "   Token B Decimals: $TOKEN_B_DECIMALS"
echo "   Ratio: $RATIO"
echo "   URL: $URL"
echo ""

# Make the request
RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$URL" \
    -H "Content-Type: application/json" \
    -d "$JSON_PAYLOAD" \
    --connect-timeout 10 \
    --max-time 60)

# Extract HTTP status code and response body
HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
RESPONSE_BODY=$(echo "$RESPONSE" | head -n -1)

# Check HTTP status
if [[ "$HTTP_CODE" -ne 200 ]]; then
    echo "❌ HTTP request failed with status: $HTTP_CODE"
    echo "Response: $RESPONSE_BODY"
    exit 1
fi

# Parse JSON response
if command -v jq >/dev/null 2>&1; then
    # Use jq if available for better JSON parsing
    ERROR_CODE=$(echo "$RESPONSE_BODY" | jq -r '.error.code // empty')
    ERROR_MESSAGE=$(echo "$RESPONSE_BODY" | jq -r '.error.message // empty')
    
    if [[ -n "$ERROR_CODE" ]]; then
        echo "❌ Pool creation failed:"
        echo "   Code: $ERROR_CODE"
        echo "   Message: $ERROR_MESSAGE"
        exit 1
    fi
    
    POOL_ID=$(echo "$RESPONSE_BODY" | jq -r '.result.pool_id // empty')
    TOKEN_A_MINT=$(echo "$RESPONSE_BODY" | jq -r '.result.token_a_mint // empty')
    TOKEN_B_MINT=$(echo "$RESPONSE_BODY" | jq -r '.result.token_b_mint // empty')
    RATIO_DISPLAY=$(echo "$RESPONSE_BODY" | jq -r '.result.ratio_display // empty')
    CREATION_SIG=$(echo "$RESPONSE_BODY" | jq -r '.result.creation_signature // empty')
    
    if [[ -n "$POOL_ID" ]]; then
        echo "✅ Pool created successfully!"
        echo "   Pool ID: $POOL_ID"
        echo "   Token A Mint: $TOKEN_A_MINT"
        echo "   Token B Mint: $TOKEN_B_MINT"
        echo "   Ratio Display: $RATIO_DISPLAY"
        echo "   Creation Signature: $CREATION_SIG"
        echo ""
        
        # Output full response for scripting
        echo "$RESPONSE_BODY" | jq .
    else
        echo "⚠️ Unexpected response format"
        echo "$RESPONSE_BODY" | jq .
    fi
else
    # Fallback without jq
    if echo "$RESPONSE_BODY" | grep -q '"error"'; then
        echo "❌ Pool creation failed. Response:"
        echo "$RESPONSE_BODY"
        exit 1
    elif echo "$RESPONSE_BODY" | grep -q '"result"'; then
        echo "✅ Pool created successfully!"
        echo "$RESPONSE_BODY"
    else
        echo "⚠️ Unexpected response format:"
        echo "$RESPONSE_BODY"
    fi
fi
