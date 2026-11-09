using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MWL4.Infrastructure
{
    /// <summary>The check at program start, which the user only hears about if it finds a new version.</summary>
    public static class StartupUpdateCheck
    {
        /// <summary>
        /// The new version, or null in every other case: turned off, up to date, failed or cancelled. Never throws, because
        /// an exception on the UI thread would reach the error message in App.
        /// </summary>
        /// <param name="enabled">When false, nothing is sent.</param>
        public static async Task<UpdateCheckResult> FindNewVersionAsync(bool enabled, UpdateChecker checker, CancellationToken token)
        {
            if (!enabled) return null;

            try
            {
                var result = await checker.CheckAsync(token);
                return result.Outcome == UpdateCheckOutcome.NewVersion ? result : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.ForContext(typeof(StartupUpdateCheck)).Warning(ex, "Version check at startup failed");
                return null;
            }
        }
    }
}
