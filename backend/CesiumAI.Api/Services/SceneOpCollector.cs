using CesiumAI.Api.Models;

namespace CesiumAI.Api.Services;

public interface ISceneOpSink
{
    void Add(SceneOp operation);
}

public sealed class SceneOpCollector : ISceneOpSink
{
    private readonly object _gate = new();
    private List<SceneOp> _operations = [];

    public void Add(SceneOp operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_gate)
        {
            _operations.Add(operation);
        }
    }

    public IReadOnlyList<SceneOp> Drain()
    {
        lock (_gate)
        {
            SceneOp[] result = [.. _operations];
            _operations = [];
            return result;
        }
    }
}
