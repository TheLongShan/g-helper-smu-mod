using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GHelper.Helpers
{
    public static class NvpcfHelper
    {
        private static string? _cachedInstanceId = null;
        private static readonly object _lock = new object();

        /// <summary>
        /// Retrieves the NVPCF device instance ID, caching it once discovered.
        /// </summary>
        public static string GetInstanceId()
        {
            if (!string.IsNullOrEmpty(_cachedInstanceId))
                return _cachedInstanceId;

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedInstanceId))
                    return _cachedInstanceId;

                try
                {
                    string output = ProcessHelper.RunCMD("pnputil", "/enum-devices /deviceid \"ACPI\\NVDA0820\"", timeoutMs: 4000);
                    var match = Regex.Match(output, @"Instance ID:\s*(.+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        _cachedInstanceId = match.Groups[1].Value.Trim();
                        Logger.WriteLine($"Found NVPCF Instance ID: {_cachedInstanceId}");
                        return _cachedInstanceId;
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Error querying NVPCF Instance ID: {ex.Message}");
                }

                _cachedInstanceId = @"ACPI\NVDA0820\NPCF";
                return _cachedInstanceId;
            }
        }

        /// <summary>
        /// Checks whether the NVPCF device exists on the current system.
        /// </summary>
        public static bool IsSupported()
        {
            try
            {
                string id = GetInstanceId();
                string output = ProcessHelper.RunCMD("pnputil", $"/enum-devices /instanceid \"{id}\"", timeoutMs: 4000);
                if (output.Contains("NVDA0820", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Platform Controllers and Framework", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"NVPCF IsSupported error: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Returns true if NVPCF is running/started (DB lock active), false if disabled (DB lock bypassed), or null on unknown.
        /// </summary>
        public static bool? IsEnabled()
        {
            try
            {
                string id = GetInstanceId();
                string output = ProcessHelper.RunCMD("pnputil", $"/enum-devices /instanceid \"{id}\"", timeoutMs: 4000);

                if (output.Contains("Status:                     Started", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Status: Started", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("已启动", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (output.Contains("Status:                     Disabled", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Status: Disabled", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("已禁用", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"NVPCF IsEnabled check error: {ex.Message}");
            }

            // Fallback via PowerShell
            try
            {
                string id = GetInstanceId();
                string ps = ProcessHelper.RunCMD("powershell", $"(Get-PnpDevice -InstanceId '{id}').Status", timeoutMs: 5000);
                if (ps.Contains("OK", StringComparison.OrdinalIgnoreCase)) return true;
                if (ps.Contains("Error", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Enables the NVPCF device.
        /// </summary>
        public static bool Enable()
        {
            string id = GetInstanceId();
            Logger.WriteLine($"NVPCF: Enabling {id}...");
            try
            {
                ProcessHelper.RunCMD("pnputil", $"/enable-device \"{id}\"", timeoutMs: 8000);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"pnputil enable error: {ex.Message}, trying PowerShell fallback");
                ProcessHelper.RunCMD("powershell", $"Enable-PnpDevice -InstanceId '{id}' -Confirm:$false", timeoutMs: 8000);
            }

            bool? state = IsEnabled();
            Logger.WriteLine($"NVPCF Enabled state: {state}");
            return state == true;
        }

        /// <summary>
        /// Disables the NVPCF device.
        /// </summary>
        public static bool Disable()
        {
            string id = GetInstanceId();
            Logger.WriteLine($"NVPCF: Disabling {id}...");
            try
            {
                ProcessHelper.RunCMD("pnputil", $"/disable-device \"{id}\"", timeoutMs: 8000);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"pnputil disable error: {ex.Message}, trying PowerShell fallback");
                ProcessHelper.RunCMD("powershell", $"Disable-PnpDevice -InstanceId '{id}' -Confirm:$false", timeoutMs: 8000);
            }

            bool? state = IsEnabled();
            Logger.WriteLine($"NVPCF Disabled state: {state}");
            return state == false;
        }

        /// <summary>
        /// Toggles NVPCF device between enabled and disabled.
        /// </summary>
        public static bool Toggle()
        {
            bool? current = IsEnabled();
            if (current == true)
            {
                return !Disable();
            }
            else
            {
                return Enable();
            }
        }

        private static long _lastResetTime = 0;

        /// <summary>
        /// Executes the unlock ritual: Enable NVPCF -> sleep delayMs -> Disable NVPCF.
        /// Bypasses the Dynamic Boost power ceiling lock.
        /// </summary>
        public static async Task<bool> ResetCycleAsync(int delayMs = 1000, bool force = false)
        {
            return await Task.Run(() =>
            {
                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (!force && Math.Abs(now - _lastResetTime) < 15000)
                {
                    Logger.WriteLine("[NVPCF] ResetCycle skipped (cooldown active)");
                    return IsEnabled() == false;
                }
                _lastResetTime = now;

                try
                {
                    string id = GetInstanceId();
                    Logger.WriteLine($"[NVPCF] Running unlock reset cycle (Enable -> wait {delayMs}ms -> Disable) for {id}...");

                    // 1. Enable
                    try
                    {
                        ProcessHelper.RunCMD("pnputil", $"/enable-device \"{id}\"", timeoutMs: 8000);
                    }
                    catch
                    {
                        ProcessHelper.RunCMD("powershell", $"Enable-PnpDevice -InstanceId '{id}' -Confirm:$false", timeoutMs: 8000);
                    }

                    Thread.Sleep(delayMs);

                    // 2. Disable
                    try
                    {
                        ProcessHelper.RunCMD("pnputil", $"/disable-device \"{id}\"", timeoutMs: 8000);
                    }
                    catch
                    {
                        ProcessHelper.RunCMD("powershell", $"Disable-PnpDevice -InstanceId '{id}' -Confirm:$false", timeoutMs: 8000);
                    }

                    bool? state = IsEnabled();
                    bool success = (state == false);
                    Logger.WriteLine($"[NVPCF] Unlock reset cycle finished. Success={success}, State={state}");
                    return success;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"[NVPCF] ResetCycleAsync error: {ex.Message}");
                    return false;
                }
            });
        }
    }
}
