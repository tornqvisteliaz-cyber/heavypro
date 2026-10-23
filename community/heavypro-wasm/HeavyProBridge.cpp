// HeavyPro bridge: a standalone WASM module for MSFS 2024.
//
// Lets people keep their stick bound in MSFS. When the HeavyPro app is shaping
// (L:HEAVYPRO_OVERRIDE = 1), every AXIS_*_SET event that does not carry the shaped
// value is answered at once with the shaped value from L:HEAVYPRO_SHAPED_*, so the
// aircraft ends each frame on HeavyPro's value instead of the raw stick.
// The raw stick value is published to L:HEAVYPRO_RAW_* for diagnostics.
//
// Safety: if the app stops bumping L:HEAVYPRO_ALIVE (closed or crashed), the module
// stops overriding after a short burst of stick events, so the stick never freezes.

#include <MSFS/MSFS.h>
#include <MSFS/MSFS_Events.h>
#include <MSFS/MSFS_Vars.h>
#include <MSFS/Types/MSFS_EventsEnum.h>

namespace
{
    // Stick events seen while ALIVE did not change before the module gives up.
    // The app bumps ALIVE about ten times a second; a moving stick sends up to one event per frame.
    constexpr int StaleEventLimit = 120;

    struct Axis
    {
        FsEventId event;
        const char* raw;
        const char* shaped;
        FsLVarId rawId;
        FsLVarId shapedId;
    };

    Axis g_axes[] = {
        { KEY_AXIS_ELEVATOR_SET, "HEAVYPRO_RAW_ELEV", "HEAVYPRO_SHAPED_ELEV", -1, -1 },
        { KEY_AXIS_AILERONS_SET, "HEAVYPRO_RAW_AIL", "HEAVYPRO_SHAPED_AIL", -1, -1 },
        { KEY_AXIS_RUDDER_SET, "HEAVYPRO_RAW_RUD", "HEAVYPRO_SHAPED_RUD", -1, -1 },
    };

    FsUnitId g_number = -1;
    FsLVarId g_override = -1;
    FsLVarId g_alive = -1;
    FsLVarId g_version = -1;
    FsLVarId g_overrides = -1;

    double g_lastAlive = -1;
    int g_staleEvents = 0;
    double g_overrideCount = 0;
    bool g_inTrigger = false;

    double Get(FsLVarId id)
    {
        double value = 0;
        fsVarsLVarGet(id, g_number, &value);
        return value;
    }

    bool ReadValue(const FsVarParamArray* param, int& value, bool& isDouble)
    {
        if (param == nullptr || param->size == 0 || param->array == nullptr)
            return false;
        const FsVarParamVariant& first = param->array[0];
        if (first.type == FsVarParamTypeInteger)
        {
            value = static_cast<int>(first.intValue);
            isDouble = false;
            return true;
        }
        if (first.type == FsVarParamTypeDouble)
        {
            value = static_cast<int>(first.doubleValue);
            isDouble = true;
            return true;
        }
        return false;
    }

    bool AppAlive()
    {
        const double alive = Get(g_alive);
        if (alive != g_lastAlive)
        {
            g_lastAlive = alive;
            g_staleEvents = 0;
            return alive > 0;
        }
        return ++g_staleEvents < StaleEventLimit && alive > 0;
    }

    void OnKeyEvent(FsEventId eventId, FsVarParamArray* param, void*)
    {
        if (g_inTrigger)
            return;
        // Loading a flight clears L:vars; say again that the bridge is here.
        if (Get(g_version) < 0.5)
            fsVarsLVarSet(g_version, g_number, 1);

        for (Axis& axis : g_axes)
        {
            if (axis.event != eventId)
                continue;

            int value = 0;
            bool isDouble = false;
            if (!ReadValue(param, value, isDouble))
                return;

            const int shaped = static_cast<int>(Get(axis.shapedId));
            // Our own trigger and the app's SimConnect send both carry the shaped value.
            if (value == shaped)
                return;

            fsVarsLVarSet(axis.rawId, g_number, value);
            if (Get(g_override) < 0.5 || !AppAlive())
                return;

            FsVarParamVariant variant{};
            if (isDouble)
            {
                variant.type = FsVarParamTypeDouble;
                variant.doubleValue = shaped;
            }
            else
            {
                variant.type = FsVarParamTypeInteger;
                variant.intValue = static_cast<unsigned int>(shaped);
            }
            FsVarParamArray out;
            out.size = 1;
            out.array = &variant;

            g_inTrigger = true;
            fsEventsTriggerKeyEvent(eventId, out);
            g_inTrigger = false;

            g_overrideCount = g_overrideCount >= 1000000 ? 1 : g_overrideCount + 1;
            fsVarsLVarSet(g_overrides, g_number, g_overrideCount);
            return;
        }
    }
}

extern "C" MSFS_CALLBACK void module_init(void)
{
    g_number = fsVarsGetUnitId("number");
    g_override = fsVarsRegisterLVar("HEAVYPRO_OVERRIDE");
    g_alive = fsVarsRegisterLVar("HEAVYPRO_ALIVE");
    g_version = fsVarsRegisterLVar("HEAVYPRO_WASM_VERSION");
    g_overrides = fsVarsRegisterLVar("HEAVYPRO_WASM_OVERRIDES");
    for (Axis& axis : g_axes)
    {
        axis.rawId = fsVarsRegisterLVar(axis.raw);
        axis.shapedId = fsVarsRegisterLVar(axis.shaped);
    }
    // Tells the app the bridge is loaded.
    fsVarsLVarSet(g_version, g_number, 1);
    fsEventsRegisterKeyEventHandler(OnKeyEvent, nullptr);
}

extern "C" MSFS_CALLBACK void module_deinit(void)
{
    fsEventsUnregisterKeyEventHandler(OnKeyEvent, nullptr);
    fsVarsLVarSet(g_version, g_number, 0);
}
