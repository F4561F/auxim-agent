using System.Text.Json;
using Auxim.Core.Config;
using Auxim.Core.Tools;

namespace Auxim.Tools;

public static class TodoTools
{
    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "todo.add",
            "todo",
            "Adds a todo item.",
            (args, _) =>
            {
                var text = FileTools.Required(args, "text");
                var todos = LoadTodos();
                var todo = new TodoItem(Guid.NewGuid().ToString("N")[..8], text, false, DateTimeOffset.UtcNow);
                todos.Add(todo);
                SaveTodos(todos);
                return Task.FromResult($"Added todo {todo.Id}: {todo.Text}");
            })
        {
            ParametersSchema = FileTools.ObjectSchema([("text", "string", "Todo text.")], ["text"]),
        });

        registry.Register(new ToolDefinition(
            "todo.list",
            "todo",
            "Lists todo items.",
            (_, _) =>
            {
                var todos = LoadTodos();
                if (todos.Count == 0)
                {
                    return Task.FromResult("No todos.");
                }

                return Task.FromResult(string.Join(Environment.NewLine, todos.Select(todo =>
                    $"{(todo.Done ? "x" : " ")} {todo.Id} {todo.Text}")));
            }));

        registry.Register(new ToolDefinition(
            "todo.done",
            "todo",
            "Marks a todo item as done.",
            (args, _) =>
            {
                var id = FileTools.Required(args, "id");
                var todos = LoadTodos();
                var todo = todos.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Todo '{id}' not found.");
                todo.Done = true;
                SaveTodos(todos);
                return Task.FromResult($"Marked done: {todo.Id}");
            })
        {
            ParametersSchema = FileTools.ObjectSchema([("id", "string", "Todo id.")], ["id"]),
        });
    }

    private static List<TodoItem> LoadTodos()
    {
        var path = TodoPath();
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions()) ?? [];
    }

    private static void SaveTodos(List<TodoItem> todos)
    {
        var path = TodoPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(todos, JsonOptions()) + Environment.NewLine);
    }

    private static string TodoPath() => Path.Combine(ConfigLoader.GetAuximHome(), "todos.json");

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed class TodoItem
    {
        public TodoItem(string id, string text, bool done, DateTimeOffset createdAt)
        {
            Id = id;
            Text = text;
            Done = done;
            CreatedAt = createdAt;
        }

        public string Id { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
