using System.Text.Json;
using WorkflowAutomation.Workflows.Core.Definitions;
using Xunit;

namespace WorkflowAutomation.Workflows.Core.Tests;

public class WorkflowDefinitionSerializerTests
{
    private static StepDefinition Step(
        string id,
        string type = "Log",
        string[]? dependsOn = null,
        Dictionary<string, string>? parameters = null)
    {
        return new StepDefinition
        {
            Id = id,
            Type = type,
            DependsOn = dependsOn ?? [],
            Parameters = parameters ?? new Dictionary<string, string>()
        };
    }

    private static WorkflowDefinition Workflow(params StepDefinition[] steps)
    {
        return new WorkflowDefinition
        {
            Steps = steps
        };
    }

    private static void AssertDefinitionEqual(WorkflowDefinition expected, WorkflowDefinition actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Steps.Count, actual.Steps.Count);

        for (var i = 0; i < expected.Steps.Count; i++)
        {
            var expectedStep = expected.Steps[i];
            var actualStep = actual.Steps[i];

            Assert.Equal(expectedStep.Id, actualStep.Id);
            Assert.Equal(expectedStep.Type, actualStep.Type);
            Assert.Equal(expectedStep.DependsOn, actualStep.DependsOn);
            Assert.Equal(expectedStep.Parameters, actualStep.Parameters);
        }
    }

    [Fact]
    public void Empty_definition_round_trips_successfully()
    {
        var original = new WorkflowDefinition();

        var json = WorkflowDefinitionSerializer.Serialize(original);
        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        Assert.NotNull(restored);
        Assert.Empty(restored.Steps);
    }

    [Fact]
    public void Linear_graph_round_trips_successfully()
    {
        var original = Workflow(
            Step("step-1", "HttpRequest"),
            Step("step-2", "Transform", dependsOn: ["step-1"]),
            Step("step-3", "SendEmail", dependsOn: ["step-2"])
        );

        var json = WorkflowDefinitionSerializer.Serialize(original);
        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        AssertDefinitionEqual(original, restored);
    }

    [Fact]
    public void Diamond_graph_round_trips_successfully()
    {
        var original = Workflow(
            Step("A", "Trigger"),
            Step("B", "Branch1", dependsOn: ["A"]),
            Step("C", "Branch2", dependsOn: ["A"]),
            Step("D", "Merge", dependsOn: ["B", "C"])
        );

        var json = WorkflowDefinitionSerializer.Serialize(original);
        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        AssertDefinitionEqual(original, restored);
    }

    [Fact]
    public void Disconnected_graph_round_trips_successfully()
    {
        var original = Workflow(
            Step("A", "Job1"),
            Step("B", "Job2", dependsOn: ["A"]),
            Step("C", "IndependentJob"),
            Step("D", "AnotherJob")
        );

        var json = WorkflowDefinitionSerializer.Serialize(original);
        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        AssertDefinitionEqual(original, restored);
    }

    [Fact]
    public void String_parameters_are_preserved_in_round_trip()
    {
        var parameters = new Dictionary<string, string>
        {
            ["url"] = "https://api.example.com/notify",
            ["method"] = "POST",
            ["body"] = "{\"msg\":\"hello\"}"
        };

        var original = Workflow(
            Step("step-1", "HttpRequest", parameters: parameters)
        );

        var json = WorkflowDefinitionSerializer.Serialize(original);
        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        AssertDefinitionEqual(original, restored);
    }

    [Fact]
    public void Missing_depends_on_defaults_to_empty_collection()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "type": "Log"
            }
          ]
        }
        """;

        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        Assert.Single(restored.Steps);
        Assert.NotNull(restored.Steps[0].DependsOn);
        Assert.Empty(restored.Steps[0].DependsOn);
    }

    [Fact]
    public void Missing_parameters_defaults_to_empty_dictionary()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "type": "Log"
            }
          ]
        }
        """;

        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        Assert.Single(restored.Steps);
        Assert.NotNull(restored.Steps[0].Parameters);
        Assert.Empty(restored.Steps[0].Parameters);
    }
    [Fact]
    public void Malformed_json_throws_json_exception()
    {
        const string invalidJson = "{ invalid json ";

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                invalidJson,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("123")]
    [InlineData("true")]
    public void Non_object_or_null_root_throws_json_exception(string invalidJson)
    {
        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                invalidJson,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Fact]
    public void Missing_steps_property_throws_json_exception()
    {
        const string json = "{}";

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Fact]
    public void Explicitly_null_steps_property_throws_json_exception()
    {
        const string json = """{ "steps": null }""";

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }
    [Fact]
    public void Null_step_entry_throws_json_exception()
    {
        const string json = """{ "steps": [null] }""";

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Theory]
    [InlineData("""{ "steps": [{ "type": "Log" }] }""")]
    [InlineData("""{ "steps": [{ "id": null, "type": "Log" }] }""")]
    [InlineData("""{ "steps": [{ "id": "", "type": "Log" }] }""")]
    [InlineData("""{ "steps": [{ "id": "   ", "type": "Log" }] }""")]
    public void Missing_null_empty_or_whitespace_step_id_throws_json_exception(string json)
    {
        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }
    [Fact]
    public void Explicitly_null_depends_on_throws_json_exception()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "dependsOn": null
            }
          ]
        }
        """;

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Fact]
    public void Null_dependency_entry_throws_json_exception()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "dependsOn": [null]
            }
          ]
        }
        """;

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Fact]
    public void Explicitly_null_parameters_throws_json_exception()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "parameters": null
            }
          ]
        }
        """;

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Fact]
    public void Null_parameter_value_throws_json_exception()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "parameters": {
                "key": null
              }
            }
          ]
        }
        """;

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }

    [Theory]
    [InlineData("""{ "steps": [{ "id": "s1", "parameters": { "k": 42 } }] }""")]
    [InlineData("""{ "steps": [{ "id": "s1", "parameters": { "k": true } }] }""")]
    [InlineData("""{ "steps": [{ "id": "s1", "parameters": { "k": {} } }] }""")]
    [InlineData("""{ "steps": [{ "id": "s1", "parameters": { "k": [] } }] }""")]
    public void Non_string_parameter_value_throws_json_exception(string json)
    {
        Assert.ThrowsAny<JsonException>(() =>
            WorkflowDefinitionSerializer.Deserialize(
                json,
                WorkflowDefinitionSerializer.CurrentSchemaVersion));
    }
    [Fact]
    public void Serialize_null_definition_throws_argument_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WorkflowDefinitionSerializer.Serialize(null!));
    }

    [Fact]
    public void Serialize_null_steps_collection_throws_json_exception()
    {
        var definition = new WorkflowDefinition
        {
            Steps = null!
        };

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Fact]
    public void Serialize_null_step_entry_throws_json_exception()
    {
        var definition = new WorkflowDefinition
        {
            Steps = [null!]
        };

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Serialize_blank_or_null_step_id_throws_json_exception(string? id)
    {
        var definition = Workflow(
            new StepDefinition
            {
                Id = id!,
                Type = "Log"
            }
        );

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Fact]
    public void Serialize_null_depends_on_throws_json_exception()
    {
        var definition = Workflow(
            new StepDefinition
            {
                Id = "step-1",
                Type = "Log",
                DependsOn = null!
            }
        );

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Fact]
    public void Serialize_null_dependency_entry_throws_json_exception()
    {
        var definition = Workflow(
            new StepDefinition
            {
                Id = "step-1",
                Type = "Log",
                DependsOn = [null!]
            }
        );

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Fact]
    public void Serialize_null_parameters_throws_json_exception()
    {
        var definition = Workflow(
            new StepDefinition
            {
                Id = "step-1",
                Type = "Log",
                Parameters = null!
            }
        );

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Fact]
    public void Serialize_null_parameter_value_throws_json_exception()
    {
        var definition = Workflow(
            new StepDefinition
            {
                Id = "step-1",
                Type = "Log",
                Parameters = new Dictionary<string, string>
                {
                    ["key"] = null!
                }
            }
        );

        Assert.Throws<JsonException>(() =>
            WorkflowDefinitionSerializer.Serialize(definition));
    }

    [Fact]
    public void Current_schema_version_succeeds()
    {
        const string json = """{ "steps": [] }""";

        var definition = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        Assert.NotNull(definition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Unsupported_schema_version_throws_not_supported_exception(int invalidVersion)
    {
        const string json = """{ "steps": [] }""";

        Assert.Throws<NotSupportedException>(() =>
            WorkflowDefinitionSerializer.Deserialize(json, invalidVersion));
    }
    [Fact]
    public void Deserialization_returns_fresh_definition_and_step_instances()
    {
        var parameters = new Dictionary<string, string>
        {
            ["connection"] = "conn-123",
            ["query"] = "SELECT 1"
        };

        var originalStep = Step(
            "step-1",
            "SqlExecute",
            dependsOn: ["step-0"],
            parameters: parameters);

        var original = Workflow(originalStep);

        var json = WorkflowDefinitionSerializer.Serialize(original);
        var restored = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        // Verify reference isolation (snapshots are independent instances)
        Assert.NotSame(original, restored);
        Assert.NotSame(original.Steps, restored.Steps);
        Assert.NotSame(originalStep, restored.Steps[0]);
        Assert.NotSame(originalStep.DependsOn, restored.Steps[0].DependsOn);
        Assert.NotSame(originalStep.Parameters, restored.Steps[0].Parameters);

        // Verify logical equality (values and contents preserved)
        AssertDefinitionEqual(original, restored);
    }

    [Fact]
    public void Deserializing_same_json_repeatedly_produces_distinct_instances()
    {
        const string json = """
        {
          "steps": [
            {
              "id": "step-1",
              "type": "Log"
            }
          ]
        }
        """;

        var first = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        var second = WorkflowDefinitionSerializer.Deserialize(
            json,
            WorkflowDefinitionSerializer.CurrentSchemaVersion);

        Assert.NotSame(first, second);
        Assert.NotSame(first.Steps, second.Steps);
        Assert.NotSame(first.Steps[0], second.Steps[0]);
        AssertDefinitionEqual(first, second);
    }

}
