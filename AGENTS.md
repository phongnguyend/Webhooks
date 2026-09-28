# Coding conventions

- Keep each persistence entity class in its own file, named after the class, under `backend/SharePointAgent.Persistence/Entities/`. This includes Identity entities such as `ApplicationUser`. Do not combine entity classes in a shared `Entities.cs` file.
- Always use braces for control-flow bodies, even when the body contains only one statement. This applies to `if`, `else if`, `else`, `for`, `foreach`, `while`, `do`, and similar constructs in languages that support braces.
- Put the body on separate lines rather than writing a single-line control-flow statement. Follow the surrounding language's brace placement style.
- Never compress a braced block onto one line, such as `if (condition) { DoSomething(); return; }`. Put each statement on its own line. In C#, put opening and closing braces on separate lines as well.
- Apply this convention to new and modified code, including tests.

Preferred C# formatting:

```csharp
if (context.User.Identity?.IsAuthenticated != true)
{
    context.Response.StatusCode = 401;
    return;
}
```
