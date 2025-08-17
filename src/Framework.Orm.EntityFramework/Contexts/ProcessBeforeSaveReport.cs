// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record ProcessBeforeSaveReport
{
    public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

    public List<EmitterLocalMessages> LocalEmitters { get; } = [];

    public void ClearEmitterMessages()
    {
        foreach (var emitter in DistributedEmitters)
        {
            emitter.Emitter.ClearDistributedMessages();
        }

        foreach (var emitter in LocalEmitters)
        {
            emitter.Emitter.ClearLocalMessages();
        }
    }
}
