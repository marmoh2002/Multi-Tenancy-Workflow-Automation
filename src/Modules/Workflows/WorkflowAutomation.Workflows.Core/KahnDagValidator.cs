using WorkflowAutomation.Workflows.Core.Definitions;

namespace WorkflowAutomation.Workflows.Core;

public sealed class KahnDagValidator : IDagValidator
{
    public DagValidationResult Validate(WorkflowDefinition workflow)
    {
        var errors = new List<string>();

        // Empty workflow is considered valid.
        if (workflow.Steps.Count == 0)
        {
            return DagValidationResult.Success([]);
        }

        // 1. Basic structural validation:
        //    - no empty IDs
        //    - no duplicate IDs
        var knownIds = new HashSet<string>();

        foreach (var step in workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add("A step has an empty Id.");
            }
            else if (!knownIds.Add(step.Id))
            {
                errors.Add($"Duplicate step id '{step.Id}'.");
            }
        }

        if (errors.Count > 0)
        {
            return DagValidationResult.Failure(errors);
        }

        // 2. Dependency reference validation:
        //    - every dependency must point to an existing step
        //    - no duplicate dependencies on the same step
        foreach (var step in workflow.Steps)
        {
            var seenDependencies = new HashSet<string>();

            foreach (var dependency in step.DependsOn)
            {
                if (!knownIds.Contains(dependency))
                {
                    errors.Add($"Step '{step.Id}' depends on unknown step '{dependency}'.");
                }

                if (!seenDependencies.Add(dependency))
                {
                    errors.Add($"Step '{step.Id}' lists dependency '{dependency}' more than once.");
                }
            }
        }

        if (errors.Count > 0)
        {
            return DagValidationResult.Failure(errors);
        }

        // 3. Build graph structures used by Kahn's algorithm.
        //
        // adjacency:
        //   key = prerequisite step
        //   value = steps that depend on it
        //
        // inDegree:
        //   how many prerequisites a step still has remaining
        var adjacency = workflow.Steps.ToDictionary(
            step => step.Id,
            _ => new List<string>());

        var inDegree = workflow.Steps.ToDictionary(
            step => step.Id,
            _ => 0);

        foreach (var step in workflow.Steps)
        {
            foreach (var dependency in step.DependsOn)
            {
                adjacency[dependency].Add(step.Id);
                inDegree[step.Id]++;
            }
        }

        // 4. Start with all steps that have no dependencies.
        var ready = new Queue<string>();

        foreach (var step in workflow.Steps)
        {
            if (inDegree[step.Id] == 0)
            {
                ready.Enqueue(step.Id);
            }
        }

        // 5. Process nodes in dependency order.
        var orderedStepIds = new List<string>();

        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            orderedStepIds.Add(current);

            foreach (var dependent in adjacency[current])
            {
                inDegree[dependent]--;

                if (inDegree[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        // 6. If not every step was processed, there is a cycle.
        if (orderedStepIds.Count != workflow.Steps.Count)
        {
            var processed = new HashSet<string>(orderedStepIds);
            // detect culprit steps (those that are not processed)
            var stuck = workflow.Steps
                .Select(step => step.Id)
                .Where(id => !processed.Contains(id))
                .ToList();

            errors.Add($"Cycle detected involving steps: {string.Join(", ", stuck)}.");

            return DagValidationResult.Failure(errors);
        }

        return DagValidationResult.Success(orderedStepIds);
    }
}