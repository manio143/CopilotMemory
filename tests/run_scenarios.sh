#!/usr/bin/env bash
# copilot-memory integration test harness
# Runs scenarios through Copilot CLI, captures event logs + produces summary
#
# Usage: bash run_scenarios.sh [output_dir]
# Output: one directory per scenario with events.jsonl, summary.md

set -euo pipefail

OUTPUT_DIR="${1:-$(pwd)/test-results/$(date +%Y%m%d-%H%M%S)}"
MODEL="gpt-5-mini"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

mkdir -p "$OUTPUT_DIR"

# Helper: run a copilot prompt and capture JSON events
run_prompt() {
    local scenario_dir="$1"
    local turn_num="$2"
    local prompt="$3"
    local session_flag="${4:-}"

    local outfile="$scenario_dir/turn${turn_num}_events.jsonl"

    echo "  Turn $turn_num: $prompt"

    local cmd="copilot -p \"$prompt\" --model $MODEL --output-format json --no-custom-instructions -s"
    if [[ -n "$session_flag" ]]; then
        cmd="$cmd --resume=$session_flag"
    fi

    eval "$cmd" > "$outfile" 2>"$scenario_dir/turn${turn_num}_stderr.log" || true

    # Extract non-ephemeral, non-delta events for readability
    jq -c 'select(.ephemeral != true and (.type | test("delta") | not))' \
        "$outfile" > "$scenario_dir/turn${turn_num}_events_filtered.jsonl" 2>/dev/null || true

    # Extract session ID for continuation
    jq -r 'select(.type == "session.start") | .data.sessionId // empty' \
        "$outfile" 2>/dev/null | head -1
}

# Helper: extract key info from a turn's events
summarize_turn() {
    local events_file="$1"
    local turn_num="$2"

    echo "### Turn $turn_num"
    echo ""

    # User message
    echo "**User prompt:**"
    jq -r 'select(.type == "user.message") | .data.content // empty' "$events_file" 2>/dev/null | head -1
    echo ""

    # Assistant response (combine message events)
    echo "**Assistant response:**"
    jq -r 'select(.type == "assistant.message") | .data.content // empty' "$events_file" 2>/dev/null
    echo ""

    # Tools used
    local tools
    tools=$(jq -r 'select(.type == "tool.execution_start") | .data.toolName // .data.name // empty' "$events_file" 2>/dev/null | sort -u)
    if [[ -n "$tools" ]]; then
        echo "**Tools invoked:** $tools"
        echo ""
    fi

    # Event type sequence
    echo "**Event sequence:**"
    jq -r '.type' "$events_file" 2>/dev/null | paste -sd ' → ' -
    echo ""
    echo "---"
    echo ""
}

# Helper: generate scenario summary
generate_summary() {
    local scenario_dir="$1"
    local scenario_name="$2"
    local description="$3"
    local expected="$4"
    local summary_file="$scenario_dir/summary.md"

    {
        echo "# Scenario: $scenario_name"
        echo ""
        echo "**Description:** $description"
        echo ""
        echo "**Expected outcome:** $expected"
        echo ""
        echo "**Timestamp:** $(date -u +%Y-%m-%dT%H:%M:%SZ)"
        echo "**Model:** $MODEL"
        echo ""
        echo "---"
        echo ""

        # Summarize each turn
        for f in "$scenario_dir"/turn*_events_filtered.jsonl; do
            [[ -f "$f" ]] || continue
            local turn
            turn=$(basename "$f" | grep -oP 'turn\K\d+')
            summarize_turn "$f" "$turn"
        done

        echo "## Verification Checklist"
        echo ""
        echo "- [ ] Events flowed in expected order"
        echo "- [ ] $expected"
        echo ""
        echo "## Raw Files"
        echo ""
        for f in "$scenario_dir"/*; do
            echo "- \`$(basename "$f")\`"
        done
    } > "$summary_file"
}


###############################################################################
# SCENARIOS
###############################################################################

echo "=== copilot-memory integration tests ==="
echo "Output: $OUTPUT_DIR"
echo ""

# ─────────────────────────────────────────────────────────────────────────────
# Scenario 1: Fact Extraction — user states preferences
# ─────────────────────────────────────────────────────────────────────────────
echo "[1/5] Fact Extraction — user states preferences"
S1_DIR="$OUTPUT_DIR/01-fact-extraction"
mkdir -p "$S1_DIR"

S1_SID=$(run_prompt "$S1_DIR" 1 \
    "My name is Marian. I'm a .NET developer and I prefer using Vim because of its keyboard-driven workflow. I use 4-space indentation, never tabs.")

# Now use gpt-5-mini to extract facts (simulating what our library would do)
EXTRACT_PROMPT='Extract facts from this conversation as JSON {"facts": [...]}.

Conversation:
user: My name is Marian. I am a .NET developer and I prefer using Vim because of its keyboard-driven workflow. I use 4-space indentation, never tabs.
assistant: (see previous turn)

Return ONLY the JSON, nothing else.'

run_prompt "$S1_DIR" 2 "$EXTRACT_PROMPT" > /dev/null

generate_summary "$S1_DIR" "Fact Extraction" \
    "User states multiple preferences in one message. Verify Copilot processes the message and we can see the full event lifecycle." \
    "user.message → assistant.turn_start → assistant.message → assistant.turn_end events present. Extraction turn produces parseable JSON with facts."

# ─────────────────────────────────────────────────────────────────────────────
# Scenario 2: Memory Recall Simulation — search should match
# ─────────────────────────────────────────────────────────────────────────────
echo "[2/5] Memory Recall Simulation"
S2_DIR="$OUTPUT_DIR/02-memory-recall"
mkdir -p "$S2_DIR"

# Turn 1: Provide context with "memories"
RECALL_PROMPT='I am going to provide you with some context from memory, then ask a question.

<relevant-memories>
- [user] Name is Marian
- [user] Prefers Vim for keyboard-driven workflow
- [user] Uses 4-space indentation, never tabs
- [assistant] Recommended DuckDB for vector storage
</relevant-memories>

Based on the memories above, what editor does the user prefer and why?'

run_prompt "$S2_DIR" 1 "$RECALL_PROMPT" > /dev/null

generate_summary "$S2_DIR" "Memory Recall Simulation" \
    "Inject pre-formatted memories as additionalContext, ask a question that requires using them. Validates the recall format works." \
    "Assistant response references Vim and keyboard-driven workflow from the injected memories."

# ─────────────────────────────────────────────────────────────────────────────
# Scenario 3: Contradicting Facts — update/replace behavior
# ─────────────────────────────────────────────────────────────────────────────
echo "[3/5] Contradicting Facts — update behavior"
S3_DIR="$OUTPUT_DIR/03-contradicting-facts"
mkdir -p "$S3_DIR"

UPDATE_PROMPT='You are a smart memory manager. Compare new facts against existing memories.
For each, decide: ADD, UPDATE, DELETE, or NONE.

Current memory:
[
    {"id": "1", "text": "Preferred database is PostgreSQL"},
    {"id": "2", "text": "Uses 4-space indentation"},
    {"id": "3", "text": "Name is Marian"}
]

New facts: ["Switched to SQLite for the current project", "Uses 4-space indentation"]

Return JSON only:
{
    "memory": [
        {"id": "<id>", "text": "<text>", "event": "ADD|UPDATE|DELETE|NONE", "old_memory": "<if UPDATE>"}
    ]
}'

run_prompt "$S3_DIR" 1 "$UPDATE_PROMPT" > /dev/null

generate_summary "$S3_DIR" "Contradicting Facts" \
    "Present existing memories and contradicting/duplicate new facts. Verify the LLM produces correct ADD/UPDATE/DELETE/NONE decisions." \
    "PostgreSQL memory gets UPDATE or DELETE. Indentation memory gets NONE. SQLite fact gets ADD."

# ─────────────────────────────────────────────────────────────────────────────
# Scenario 4: Trivial Messages — no facts extracted
# ─────────────────────────────────────────────────────────────────────────────
echo "[4/5] Trivial Messages — empty extraction"
S4_DIR="$OUTPUT_DIR/04-trivial-messages"
mkdir -p "$S4_DIR"

TRIVIAL_PROMPT='Extract facts from this conversation as JSON {"facts": [...]}.

Conversation:
user: Hi
assistant: Hello! How can I help you today?
user: Thanks
assistant: You are welcome!
user: Ok bye
assistant: Goodbye!

Return ONLY the JSON, nothing else.'

run_prompt "$S4_DIR" 1 "$TRIVIAL_PROMPT" > /dev/null

generate_summary "$S4_DIR" "Trivial Messages" \
    "Trivial conversation with no meaningful facts. Verify extraction returns empty array." \
    "Output is {\"facts\": []} or equivalent empty result."

# ─────────────────────────────────────────────────────────────────────────────
# Scenario 5: User vs Assistant Source Separation
# ─────────────────────────────────────────────────────────────────────────────
echo "[5/5] User vs Assistant Source Separation"
S5_DIR="$OUTPUT_DIR/05-source-separation"
mkdir -p "$S5_DIR"

# Turn 1: Extract USER facts only
USER_EXTRACT='Extract facts from the USER messages only. The assistant messages are context only. Return JSON {"facts": [...]}.

Conversation:
user: I prefer PostgreSQL for production databases. What do you think about Redis?
assistant: Redis is great for caching and session storage. For your use case, I would recommend using Redis alongside PostgreSQL — Redis for hot data caching and PostgreSQL as the primary store.

Return ONLY the JSON with facts from USER messages, nothing else.'

run_prompt "$S5_DIR" 1 "$USER_EXTRACT" > /dev/null

# Turn 2: Extract ASSISTANT facts only
ASST_EXTRACT='Extract facts from the ASSISTANT messages only. The user messages are context only. Return JSON {"facts": [...]}.

Conversation:
user: I prefer PostgreSQL for production databases. What do you think about Redis?
assistant: Redis is great for caching and session storage. For your use case, I would recommend using Redis alongside PostgreSQL — Redis for hot data caching and PostgreSQL as the primary store.

Return ONLY the JSON with facts from ASSISTANT messages, nothing else.'

run_prompt "$S5_DIR" 2 "$ASST_EXTRACT" > /dev/null

generate_summary "$S5_DIR" "User vs Assistant Source Separation" \
    "Same conversation processed with user-only and assistant-only extraction prompts. Verify facts are correctly attributed." \
    "Turn 1 (user): facts about PostgreSQL preference. Turn 2 (assistant): facts about Redis recommendation. No cross-contamination."


###############################################################################
# FINAL REPORT
###############################################################################

REPORT="$OUTPUT_DIR/REPORT.md"
{
    echo "# copilot-memory Integration Test Report"
    echo ""
    echo "**Date:** $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "**Model:** $MODEL"
    echo "**Scenarios:** 5"
    echo ""
    echo "| # | Scenario | Status |"
    echo "|---|----------|--------|"
    echo "| 1 | Fact Extraction | ⬜ Review |"
    echo "| 2 | Memory Recall Simulation | ⬜ Review |"
    echo "| 3 | Contradicting Facts | ⬜ Review |"
    echo "| 4 | Trivial Messages | ⬜ Review |"
    echo "| 5 | Source Separation | ⬜ Review |"
    echo ""
    echo "## Scenario Summaries"
    echo ""
    for f in "$OUTPUT_DIR"/*/summary.md; do
        [[ -f "$f" ]] || continue
        cat "$f"
        echo ""
        echo "---"
        echo ""
    done
} > "$REPORT"

echo ""
echo "=== Done ==="
echo "Report: $REPORT"
echo "Review each scenario in: $OUTPUT_DIR/*/"
