using System;
using System.Collections.Generic;
using System.Text;

namespace FleetTelemetry.Application.Abstractions;

public interface IOutboxRecovery
{
    Task<int> RestoreAsync(CancellationToken ct);
}