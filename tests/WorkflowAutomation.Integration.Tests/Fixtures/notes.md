# Fixture
```
InitializeAsync()
    Start a temporary PostgreSQL container
    Register your application’s database services
    Apply the committed EF migrations
              ↓
Tests use CreateScope()
    Obtain services such as ApplicationDbContext
    Perform their checks
              ↓
DisposeAsync()
    Dispose services
    Dispose the temporary container

```

```
await db.Database.MigrateAsync();
```
This applies the committed migration files to the temporary database. The fixture previously used `EnsureCreatedAsync()`, which builds tables from the current EF model and ignores migration history.
