__XW_Token = false
__XW_TokenIdle = true
__XW_TokenType = "helper:ManeuverSetEditor"

local editMode = false
local moveSet = {}
local editState = {}
local initialized = false
local labelText = nil

local ANNOUNCE_COLOR = { 0.3, 0.7, 1.0 }
local DISABLED_COLOR = "#333333"
local RED_COLOR = "#8f2020"
local WHITE_COLOR = "#777777"
local GREEN_COLOR = "#269b3fff"
local ICON_ON = "#ffffffff"
local ICON_OFF = "#666666aa"
local SPEED_ASSET_BASE_URL = "https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/maneuvers/"

local ASSET_FILES = {
    Speed0 = SPEED_ASSET_BASE_URL .. "v0.png",
    Speed1 = SPEED_ASSET_BASE_URL .. "v1.png",
    Speed2 = SPEED_ASSET_BASE_URL .. "v2.png",
    Speed3 = SPEED_ASSET_BASE_URL .. "v3.png",
    Speed4 = SPEED_ASSET_BASE_URL .. "v4.png",
    Speed5 = SPEED_ASSET_BASE_URL .. "v5.png",

    GreenBankL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/green/bank-left.png",
    GreenBankR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/green/bank-right.png",
    GreenStraight = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/green/straight.png",
    GreenTurnL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/green/turn-left.png",
    GreenTurnR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/green/turn-right.png",
    RedBankL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/bank-left.png",
    RedBankR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/bank-right.png",
    RedK = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/koiogran-turn.png",
    RedReverseBankL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/reverse-bank-left.png",
    RedReverseBankR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/reverse-bank-right.png",
    RedReverseStraight = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/reverse-straight.png",
    RedSloopL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/segnors-loop-left.png",
    RedSloopR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/segnors-loop-right.png",
    RedStall = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/stop.png",
    RedStraight = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/straight.png",
    RedTalonL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/tallon-roll-left.png",
    RedTalonR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/tallon-roll-right.png",
    RedTurnL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/turn-left.png",
    RedTurnR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/red/turn-right.png",
    WhiteBankL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/bank-left.png",
    WhiteBankR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/bank-right.png",
    WhiteK = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/koiogran-turn.png",
    WhiteSloopL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/segnors-loop-left.png",
    WhiteStraight = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/straight.png",
    WhiteTurnL = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/turn-left.png",
    WhiteTurnR = "https://raw.githubusercontent.com/grimsdalee/xwing-unified-1e/refs/heads/main/assets/generated/FirstEditionManeuverIcon/white/turn-right.png",
}

local ROW_Y = {
    ["0"] = -74,
    ["1"] = -40,
    ["2"] = -6,
    ["3"] = 27,
    ["4"] = 61,
    ["5"] = 95,
}

local COLUMN_X = {
    tl = -206,
    bl = -169,
    s = -133,
    br = -97,
    tr = -60,
    blr = -10,
    brr = 27,
    tlt = 63,
    trt = 99,
    bls = 136,
    brs = 172,
    sr = 209,
    k = 245,
}

local LAYOUT = {
    { speed = "0", bases = { "bl0", "s0", "br0" } },
    { speed = "1", bases = { "tl1", "bl1", "s1", "br1", "tr1", "bl1r", "br1r", "tl1t", "tr1t", "bl1s", "br1s", "s1r", "k1" } },
    { speed = "2", bases = { "tl2", "bl2", "s2", "br2", "tr2", "tl2t", "tr2t", "bl2s", "br2s", "s2r", "k2" } },
    { speed = "3", bases = { "tl3", "bl3", "s3", "br3", "tr3", "tl3t", "tr3t", "bl3s", "br3s", "k3" } },
    { speed = "4", bases = { "s4", "k4" } },
    { speed = "5", bases = { "s5", "k5" } },
}

local slotBase = {}
local slotSpeed = {}

local function colorName(color)
    if color == "b" then
        return "Green"
    elseif color == "w" then
        return "White"
    elseif color == "r" then
        return "Red"
    end
    return "Red"
end

local function installManeuverAssets()
    local existing = self.UI.getCustomAssets() or {}
    local merged = {}
    local byName = {}

    for _, asset in ipairs(existing) do
        local copy = { name = asset.name, url = asset.url }
        merged[#merged + 1] = copy
        byName[copy.name] = copy
    end

    local changed = false
    for name, file in pairs(ASSET_FILES) do
        local url = file
        if byName[name] == nil then
            merged[#merged + 1] = { name = name, url = url }
            changed = true
        elseif byName[name].url ~= url then
            byName[name].url = url
            changed = true
        end
    end

    if changed then
        self.UI.setCustomAssets(merged)
    end
end

local function columnType(base)
    if base:sub(1, 2) == "tl" and base:sub(-1) == "t" then
        return "tlt"
    elseif base:sub(1, 2) == "tr" and base:sub(-1) == "t" then
        return "trt"
    elseif base:sub(1, 2) == "bl" and base:sub(-1) == "s" then
        return "bls"
    elseif base:sub(1, 2) == "br" and base:sub(-1) == "s" then
        return "brs"
    elseif base:sub(1, 2) == "bl" and base:sub(-1) == "r" then
        return "blr"
    elseif base:sub(1, 2) == "br" and base:sub(-1) == "r" then
        return "brr"
    elseif base:sub(1, 1) == "s" and base:sub(-1) == "r" then
        return "sr"
    elseif base:sub(1, 1) == "k" then
        return "k"
    elseif base:sub(1, 2) == "tl" then
        return "tl"
    elseif base:sub(1, 2) == "bl" then
        return "bl"
    elseif base:sub(1, 2) == "br" then
        return "br"
    elseif base:sub(1, 2) == "tr" then
        return "tr"
    end

    return "s"
end

local function iconFor(color, base)
    local name = colorName(color)

    if base:sub(1, 2) == "tl" and base:sub(-1) == "t" then
        return name .. "TalonL"
    elseif base:sub(1, 2) == "tr" and base:sub(-1) == "t" then
        return name .. "TalonR"
    elseif base:sub(1, 2) == "bl" and base:sub(-1) == "s" then
        return name .. "SloopL"
    elseif base:sub(1, 2) == "br" and base:sub(-1) == "s" then
        return name .. "SloopR"
    elseif base:sub(1, 2) == "bl" and base:sub(-1) == "r" then
        return name .. "ReverseBankL"
    elseif base:sub(1, 2) == "br" and base:sub(-1) == "r" then
        return name .. "ReverseBankR"
    elseif base:sub(1, 1) == "s" and base:sub(-1) == "r" then
        return name .. "ReverseStraight"
    elseif base:sub(1, 1) == "k" then
        return name .. "K"
    elseif base == "s0" then
        return name .. "Stall"
    elseif base:sub(1, 2) == "tl" then
        return name .. "TurnL"
    elseif base:sub(1, 2) == "bl" then
        return name .. "BankL"
    elseif base:sub(1, 2) == "br" then
        return name .. "BankR"
    elseif base:sub(1, 2) == "tr" then
        return name .. "TurnR"
    end

    return name .. "Straight"
end

local function buttonColor(color)
    if color == "r" then
        return RED_COLOR
    elseif color == "w" then
        return WHITE_COLOR
    elseif color == "b" then
        return GREEN_COLOR
    end
    return DISABLED_COLOR
end

local function buildSlotMap()
    for _, row in ipairs(LAYOUT) do
        for index, base in ipairs(row.bases) do
            local slot = "M" .. row.speed .. "_" .. tostring(index)
            slotBase[slot] = base
            slotSpeed[slot] = row.speed
        end
    end
end

local function orderedMoveSet()
    local moves = {}
    for _, row in ipairs(LAYOUT) do
        for _, base in ipairs(row.bases) do
            local color = editState[base]
            if color then
                table.insert(moves, color .. base)
            end
        end
    end
    return moves
end

local function rebuildMoveSet()
    moveSet = orderedMoveSet()
end

local function updateLabel()
    local text = self.getDescription() or ""
    if text == labelText then
        return
    end

    labelText = text
    if text == "" then
        self.UI.setAttribute("NamePanel", "active", "false")
    else
        self.UI.setAttribute("NameLabel", "text", text)
        self.UI.setAttribute("NamePanel", "active", "true")
    end
end

local function renderSlot(slot, base, speed)
    local color = editState[base]
    local x = COLUMN_X[columnType(base)]
    local y = ROW_Y[speed]

    self.UI.setAttribute(slot, "offsetXY", tostring(x) .. " " .. tostring(y))
    self.UI.setAttribute(slot, "active", (editMode or color ~= nil) and "true" or "false")
    self.UI.setAttribute(slot, "color", buttonColor(color))
    self.UI.setAttribute(slot .. "Icon", "image", iconFor(color, base))
    self.UI.setAttribute(slot .. "Icon", "color", color and ICON_ON or ICON_OFF)
end

local function render()
    for slot, base in pairs(slotBase) do
        renderSlot(slot, base, slotSpeed[slot])
    end
end

local function updateContextMenu()
    self.clearContextMenu()
    self.addContextMenuItem(editMode and "Exit edit mode" or "Enter edit mode", toggleEditMode)
end

local function buildEditStateFromMoveSet(sourceMoveSet)
    editState = {}
    for _, move in ipairs(sourceMoveSet or {}) do
        local color = move:sub(1, 1)
        if color == "r" or color == "w" or color == "b" then
            editState[move:sub(2)] = color
        end
    end
    rebuildMoveSet()
end

local function nearestShip()
    return Global.call("API_FindNearestShip", { object = self, max_distance = 100 })
end

local function initializeFromShip(ship)
    local data = ship.getTable("Data") or {}
    buildEditStateFromMoveSet(data.moveSet or {})
    initialized = true
    printToAll("Maneuver editor loaded move set from " .. ship.getName(), ANNOUNCE_COLOR)
    render()
end

local function applyToShip(ship)
    if not initialized then
        printToAll("Maneuver editor has no move set loaded", ANNOUNCE_COLOR)
        return
    end

    rebuildMoveSet()
    ship.call("setMoveSet", { moveSet = moveSet })
    printToAll("Maneuver editor applied " .. tostring(#moveSet) .. " moves to " .. ship.getName(), ANNOUNCE_COLOR)
end

local function nextColor(current)
    if current == nil then
        return "r"
    elseif current == "r" then
        return "w"
    elseif current == "w" then
        return "b"
    end
    return nil
end

function editManeuver(player, _, id)
    if not editMode then
        return
    end

    local base = slotBase[id]
    editState[base] = nextColor(editState[base])
    rebuildMoveSet()
    initialized = true
    renderSlot(id, base, slotSpeed[id])
end

function toggleEditMode()
    editMode = not editMode
    render()
    updateContextMenu()
end

function onDropped()
    local ship = nearestShip()
    if ship == nil then
        return
    end

    if editMode then
        initializeFromShip(ship)
    else
        applyToShip(ship)
    end
end

function onUpdate()
    updateLabel()
end

function onLoad(savedData)
    buildSlotMap()
    installManeuverAssets()

    if savedData and savedData ~= "" then
        local state = JSON.decode(savedData)
        editMode = state.editMode or false
        buildEditStateFromMoveSet(state.moveSet or {})
        initialized = state.initialized or #moveSet > 0
    end

    render()
    updateLabel()
    updateContextMenu()
end

function onSave()
    rebuildMoveSet()
    return JSON.encode({
        editMode = editMode,
        initialized = initialized,
        moveSet = moveSet
    })
end
