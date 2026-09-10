using System.Collections.Generic;

namespace GeneSys.AI
{
    public sealed class AiPromptQueue
    {
        private readonly List<QueuedPrompt> items = new();

        public int Count => items.Count;

        public void Enqueue(PromptKind kind, string text)
        {
            if (string.IsNullOrWhiteSpace(text) && kind == PromptKind.User) return;
            items.Add(new QueuedPrompt(kind, text ?? string.Empty));
        }

        public void EnqueueUser(string text) => Enqueue(PromptKind.User, text);

        public void EnqueueAgent(string text) => Enqueue(PromptKind.Agent, text);

        public bool TryDequeue(out QueuedPrompt prompt)
        {
            prompt = default;
            if (items.Count == 0) return false;
            int index = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind == PromptKind.User)
                {
                    index = i;
                    break;
                }
            }

            prompt = items[index];
            items.RemoveAt(index);
            return true;
        }

        public void Clear() => items.Clear();
    }

    public sealed class ActLoopMachine
    {
        public ActStep Step { get; private set; } = ActStep.Assess;
        public bool IsComplete { get; private set; }

        public void Begin()
        {
            Step = ActStep.Assess;
            IsComplete = false;
        }

        public ActStep NextStep()
        {
            if (IsComplete) return Step;
            if (Step == ActStep.Assess) Step = ActStep.Convert;
            else if (Step == ActStep.Convert) Step = ActStep.Think;
            else
                IsComplete = true;
            return Step;
        }

        public void Complete() => IsComplete = true;
    }
}
