using WorkflowAutomation.Workflows.Core.Definitions;

using Xunit;

namespace WorkflowAutomation.Workflows.Core.Tests;

public class DagValidatorTests
{
    private readonly KahnDagValidator _validator = new();

    private static StepDefinition Step(string id, params string[] dependsOn)
    {
        return new StepDefinition
        {
            Id = id,
            Type = "Log",
            DependsOn = dependsOn
        };
    }

    private static WorkflowDefinition Workflow(params StepDefinition[] steps)
    {
        return new WorkflowDefinition
        {
            Steps = steps
        };
    }

    [Fact]
    public void Empty_workflow_is_valid()
    {
        var workflow = new WorkflowDefinition();

        var result = _validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.Empty(result.OrderedStepIds);
    }

    [Fact]
    public void Valid_chain_is_valid_and_returns_expected_order()
    {
        var workflow = Workflow(
            Step("A"),
            Step("B", "A"),
            Step("C", "B")
        );

        var result = _validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.Equal(["A", "B", "C"], result.OrderedStepIds);
    }

    [Fact]
    public void Valid_diamond_is_valid_and_respects_dependencies()
    {
        var workflow = Workflow(
            Step("A"),
            Step("B", "A"),
            Step("C", "A"),
            Step("D", "B", "C")
        );

        var result = _validator.Validate(workflow);

        Assert.True(result.IsValid);

        var order = result.OrderedStepIds;

        Assert.Equal(4, order.Count);
        Assert.Contains("A", order);
        Assert.Contains("B", order);
        Assert.Contains("C", order);
        Assert.Contains("D", order);

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
        Assert.True(order.IndexOf("A") < order.IndexOf("C"));
        Assert.True(order.IndexOf("B") < order.IndexOf("D"));
        Assert.True(order.IndexOf("C") < order.IndexOf("D"));
    }

    [Fact]
    public void Disconnected_nodes_are_valid()
    {
        var workflow = Workflow(
            Step("A"),
            Step("B"),
            Step("C", "B")
        );

        var result = _validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.OrderedStepIds.Count);
        Assert.True(result.OrderedStepIds.IndexOf("B") < result.OrderedStepIds.IndexOf("C"));
    }

    [Fact]
    public void Single_node_is_valid()
    {
        var workflow = Workflow(
            Step("A")
        );

        var result = _validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.Equal(["A"], result.OrderedStepIds);
    }

    [Fact]
    public void Simple_cycle_is_invalid()
    {
        var workflow = Workflow(
            Step("A", "B"),
            Step("B", "A")
        );

        var result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Self_loop_is_invalid()
    {
        var workflow = Workflow(
            Step("A", "A")
        );

        var result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Unknown_dependency_is_invalid()
    {
        var workflow = Workflow(
            Step("A", "MISSING")
        );

        var result = _validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unknown step"));
    }
}