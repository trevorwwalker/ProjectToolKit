// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Accounting;

/// <summary>
/// The model column of an accounting row (ACCT-2, <c>Account</c> L848-849): the models.json key
/// the worker's model resolves to, else the model as configured, else the config model.
/// </summary>
public static class ModelColumn
{
    /// <summary>The model that really ran for a worker on <paramref name="workerModel"/>.</summary>
    public static string Resolve(
        IReadOnlyList<ModelEntry> models,
        string? workerModel,
        string? endpoint,
        string? configModel,
        ServerModelCache? server = null)
    {
        var key = ModelKey.Resolve(models, workerModel, endpoint, server);
        if (key.Length != 0)
        {
            return key;
        }

        if (!string.IsNullOrEmpty(workerModel))
        {
            return workerModel;
        }

        return configModel ?? "";
    }
}
