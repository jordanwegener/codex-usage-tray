using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexUsageTray
{
    internal sealed class CodexAppServer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly Dictionary<int, TaskCompletionSource<object>> _pending =
            new Dictionary<int, TaskCompletionSource<object>>();
        private readonly Dictionary<string, TaskCompletionSource<LoginCompletion>> _loginWaiters =
            new Dictionary<string, TaskCompletionSource<LoginCompletion>>();
        private readonly StringBuilder _stderr = new StringBuilder();

        private Process _process;
        private Task _startTask;
        private int _nextId = 1;
        private bool _disposed;

        public async Task<AccountStatus> ReadAccountAsync()
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            var parameters = new Dictionary<string, object> { { "refreshToken", true } };
            var result = JsonObject(await RequestAsync("account/read", parameters).ConfigureAwait(false));
            var account = JsonObject(Get(result, "account"));
            var type = GetString(account, "type");
            return new AccountStatus
            {
                SignedIn = String.Equals(type, "chatgpt", StringComparison.OrdinalIgnoreCase),
                Email = String.Equals(type, "chatgpt", StringComparison.OrdinalIgnoreCase)
                    ? GetString(account, "email")
                    : null
            };
        }

        public async Task<UsageWindows> ReadUsageAsync()
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            var result = JsonObject(await RequestAsync("account/rateLimits/read", null).ConfigureAwait(false));
            return ParseUsageWindows(result);
        }

        public async Task LoginWithChatGptAsync()
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            var parameters = new Dictionary<string, object>
            {
                { "type", "chatgpt" },
                { "codexStreamlinedLogin", true }
            };
            var result = JsonObject(await RequestAsync("account/login/start", parameters).ConfigureAwait(false));
            var type = GetString(result, "type");
            var loginId = GetString(result, "loginId");
            var authUrl = GetString(result, "authUrl");
            if (!String.Equals(type, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
                String.IsNullOrWhiteSpace(loginId) || String.IsNullOrWhiteSpace(authUrl))
            {
                throw new InvalidOperationException("Codex did not return a ChatGPT browser login URL.");
            }

            var waiter = new TaskCompletionSource<LoginCompletion>();
            lock (_gate) _loginWaiters[loginId] = waiter;
            try
            {
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                var timeout = Task.Delay(TimeSpan.FromMinutes(5));
                var completed = await Task.WhenAny(waiter.Task, timeout).ConfigureAwait(false);
                if (completed != waiter.Task) throw new TimeoutException("ChatGPT login timed out.");
                var outcome = await waiter.Task.ConfigureAwait(false);
                if (!outcome.Success)
                {
                    throw new InvalidOperationException(outcome.Error ?? "ChatGPT login failed.");
                }
            }
            finally
            {
                lock (_gate) _loginWaiters.Remove(loginId);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop(new ObjectDisposedException("CodexAppServer"));
        }

        private Task EnsureStartedAsync()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("CodexAppServer");
                if (_process != null && !_process.HasExited && _startTask != null) return _startTask;
                _startTask = StartCoreAsync();
                return _startTask;
            }
        }

        private async Task StartCoreAsync()
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "codex",
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnError;
            process.Exited += OnExited;

            lock (_gate)
            {
                _stderr.Clear();
                _process = process;
            }

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_process, process)) _process = null;
                    _startTask = null;
                }
                process.Dispose();
                throw;
            }

            var capabilities = new Dictionary<string, object>
            {
                { "experimentalApi", true },
                { "requestAttestation", false },
                { "optOutNotificationMethods", new string[0] }
            };
            var clientInfo = new Dictionary<string, object>
            {
                { "name", "codex-usage-tray" },
                { "title", "Codex Usage Tray" },
                { "version", "0.1.0" }
            };
            var parameters = new Dictionary<string, object>
            {
                { "clientInfo", clientInfo },
                { "capabilities", capabilities }
            };
            try
            {
                await RequestAsync("initialize", parameters).ConfigureAwait(false);
                Notify("initialized", null);
            }
            catch (Exception exception)
            {
                Stop(exception);
                throw;
            }
        }

        private async Task<object> RequestAsync(string method, Dictionary<string, object> parameters)
        {
            int id;
            TaskCompletionSource<object> waiter;
            lock (_gate)
            {
                if (_process == null || _process.HasExited)
                    throw new InvalidOperationException("Codex app-server is not running.");
                id = _nextId++;
                waiter = new TaskCompletionSource<object>();
                _pending[id] = waiter;
                WriteLocked(BuildMessage(method, id, parameters));
            }

            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completed = await Task.WhenAny(waiter.Task, timeout).ConfigureAwait(false);
            if (completed != waiter.Task)
            {
                lock (_gate) _pending.Remove(id);
                throw new TimeoutException("Codex timed out while handling " + method + ".");
            }
            return await waiter.Task.ConfigureAwait(false);
        }

        private void Notify(string method, Dictionary<string, object> parameters)
        {
            lock (_gate)
            {
                if (_process == null || _process.HasExited) return;
                var message = new Dictionary<string, object> { { "method", method } };
                if (parameters != null) message["params"] = parameters;
                WriteLocked(message);
            }
        }

        private static Dictionary<string, object> BuildMessage(
            string method,
            int id,
            Dictionary<string, object> parameters)
        {
            var message = new Dictionary<string, object>
            {
                { "method", method },
                { "id", id }
            };
            if (parameters != null) message["params"] = parameters;
            return message;
        }

        private void WriteLocked(Dictionary<string, object> message)
        {
            _process.StandardInput.WriteLine(_json.Serialize(message));
            _process.StandardInput.Flush();
        }

        private void OnOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            if (String.IsNullOrWhiteSpace(eventArgs.Data)) return;
            Dictionary<string, object> message;
            try
            {
                message = JsonObject(_json.DeserializeObject(eventArgs.Data));
            }
            catch
            {
                return;
            }
            if (message == null) return;

            var idValue = Get(message, "id");
            if (idValue != null)
            {
                TaskCompletionSource<object> waiter = null;
                var id = Convert.ToInt32(idValue, CultureInfo.InvariantCulture);
                lock (_gate)
                {
                    if (_pending.TryGetValue(id, out waiter)) _pending.Remove(id);
                }
                if (waiter == null) return;
                var error = JsonObject(Get(message, "error"));
                if (error != null)
                {
                    waiter.TrySetException(new InvalidOperationException(
                        GetString(error, "message") ?? "Codex returned an unknown error."));
                }
                else
                {
                    waiter.TrySetResult(Get(message, "result"));
                }
                return;
            }

            if (String.Equals(GetString(message, "method"), "account/login/completed", StringComparison.Ordinal))
            {
                var parameters = JsonObject(Get(message, "params"));
                var loginId = GetString(parameters, "loginId");
                if (String.IsNullOrWhiteSpace(loginId)) return;
                TaskCompletionSource<LoginCompletion> waiter = null;
                lock (_gate) _loginWaiters.TryGetValue(loginId, out waiter);
                if (waiter != null)
                {
                    waiter.TrySetResult(new LoginCompletion
                    {
                        Success = GetBoolean(parameters, "success"),
                        Error = GetString(parameters, "error")
                    });
                }
            }
        }

        private void OnError(object sender, DataReceivedEventArgs eventArgs)
        {
            if (String.IsNullOrWhiteSpace(eventArgs.Data)) return;
            lock (_gate)
            {
                if (_stderr.Length > 4000) _stderr.Remove(0, _stderr.Length - 3000);
                _stderr.AppendLine(eventArgs.Data);
            }
        }

        private void OnExited(object sender, EventArgs eventArgs)
        {
            string detail;
            Process process;
            lock (_gate)
            {
                process = _process;
                detail = _stderr.ToString().Trim();
                _process = null;
                _startTask = null;
            }
            var message = String.IsNullOrWhiteSpace(detail)
                ? "Codex app-server stopped unexpectedly."
                : "Codex error: " + detail;
            FailPending(new InvalidOperationException(message));
            if (process != null) process.Dispose();
        }

        private void Stop(Exception error)
        {
            Process process;
            lock (_gate)
            {
                process = _process;
                _process = null;
                _startTask = null;
            }
            if (process != null)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch { }
                process.Dispose();
            }
            FailPending(error);
        }

        private void FailPending(Exception error)
        {
            TaskCompletionSource<object>[] requestWaiters;
            TaskCompletionSource<LoginCompletion>[] loginWaiters;
            lock (_gate)
            {
                requestWaiters = _pending.Values.ToArray();
                loginWaiters = _loginWaiters.Values.ToArray();
                _pending.Clear();
                _loginWaiters.Clear();
            }
            foreach (var waiter in requestWaiters) waiter.TrySetException(error);
            foreach (var waiter in loginWaiters) waiter.TrySetException(error);
        }

        private static UsageWindows ParseUsageWindows(Dictionary<string, object> response)
        {
            var snapshots = new List<Dictionary<string, object>>();
            var bucketMap = JsonObject(Get(response, "rateLimitsByLimitId"));
            if (bucketMap != null && bucketMap.Count > 0)
            {
                var preferred = new List<Dictionary<string, object>>();
                foreach (var pair in bucketMap)
                {
                    var snapshot = JsonObject(pair.Value);
                    if (snapshot == null) continue;
                    snapshots.Add(snapshot);
                    var identity = pair.Key + " " + GetString(snapshot, "limitId") + " " + GetString(snapshot, "limitName");
                    if (identity.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0) preferred.Add(snapshot);
                }
                if (preferred.Count > 0) snapshots = preferred;
            }
            if (snapshots.Count == 0)
            {
                var legacy = JsonObject(Get(response, "rateLimits"));
                if (legacy != null) snapshots.Add(legacy);
            }

            var windows = new List<RateWindow>();
            foreach (var snapshot in snapshots)
            {
                AddWindow(windows, JsonObject(Get(snapshot, "primary")));
                AddWindow(windows, JsonObject(Get(snapshot, "secondary")));
            }
            return new UsageWindows
            {
                FiveHour = ClosestWindow(windows, 5 * 60, 60),
                Weekly = ClosestWindow(windows, 7 * 24 * 60, 24 * 60)
            };
        }

        private static void AddWindow(List<RateWindow> windows, Dictionary<string, object> value)
        {
            if (value == null) return;
            var duration = GetNullableDouble(value, "windowDurationMins");
            var used = GetNullableDouble(value, "usedPercent");
            if (!duration.HasValue || !used.HasValue) return;
            windows.Add(new RateWindow
            {
                DurationMinutes = duration.Value,
                UsedPercent = used.Value,
                ResetsAt = GetNullableLong(value, "resetsAt")
            });
        }

        private static RateWindow ClosestWindow(List<RateWindow> windows, double target, double tolerance)
        {
            RateWindow best = null;
            var bestDistance = Double.MaxValue;
            foreach (var window in windows)
            {
                var distance = Math.Abs(window.DurationMinutes - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = window;
                }
            }
            return best != null && bestDistance <= tolerance ? best : null;
        }

        private static Dictionary<string, object> JsonObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        private static object Get(Dictionary<string, object> value, string key)
        {
            object result;
            return value != null && value.TryGetValue(key, out result) ? result : null;
        }

        private static string GetString(Dictionary<string, object> value, string key)
        {
            var result = Get(value, key);
            return result == null ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
        }

        private static bool GetBoolean(Dictionary<string, object> value, string key)
        {
            var result = Get(value, key);
            return result != null && Convert.ToBoolean(result, CultureInfo.InvariantCulture);
        }

        private static double? GetNullableDouble(Dictionary<string, object> value, string key)
        {
            var result = Get(value, key);
            return result == null ? (double?)null : Convert.ToDouble(result, CultureInfo.InvariantCulture);
        }

        private static long? GetNullableLong(Dictionary<string, object> value, string key)
        {
            var result = Get(value, key);
            return result == null ? (long?)null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        private sealed class LoginCompletion
        {
            public bool Success { get; set; }
            public string Error { get; set; }
        }
    }
}

