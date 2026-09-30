namespace HexC.Server
{
    /// <summary>Runs the lobby clock (table countdowns, the turn clock) whether or not any page is open.</summary>
    public class LobbyClock : BackgroundService
    {
        private readonly ILogger<LobbyClock> _log;
        public LobbyClock(ILogger<LobbyClock> log) => _log = log;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { GameStore.Tick(DateTime.UtcNow); }
                catch (Exception e) { _log.LogError(e, "Lobby tick failed"); }
            }
        }
    }
}
