# Fixture
```
InitializeAsync()
    Start a temporary PostgreSQL container
    Register your application’s database services
    Create the database tables
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
await db.Database.EnsureCreatedAsync();
```
This creates tables directly from your current EF model. It does not read your migration files. 