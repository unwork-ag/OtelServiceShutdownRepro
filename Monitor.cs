using System.ServiceProcess;

namespace OtelServiceShutdownRepro
{
    public sealed class Monitor : IDisposable
    {
        private readonly string _serviceName;
        private readonly Serilog.ILogger _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private string _status;
        private readonly Task _task;

        public Monitor(string serviceName, Serilog.ILogger logger)
        {
            _serviceName = serviceName;
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            _logger.Information("---- monitor constructor!");
            _task = Task.Run(() => Watch(token));
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            try
            {
                _task.Wait(2000);
            }
            catch (Exception)
            {
                // swallow
            }
            _logger.Information("---- monitor dispose!");
        }

        private async Task Watch(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                ServiceController sc = new(_serviceName);
                var status = sc.Status.ToString();
                if (status != _status)
                {
                    _logger.Information("**** service status: {status}", status);
                    _status = status;
                }

                await Task.Delay(50, token);
            }
        }
    }
}
