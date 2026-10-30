namespace QuizPollApi.Infrastructure.Persistence;

public class ReadDbContext(DbContextOptions<ReadDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Response> Responses => Set<Response>();
    public DbSet<ResponseAnswer> ResponseAnswers => Set<ResponseAnswer>();
    public DbSet<QuizAnalytics> QuizAnalytics => Set<QuizAnalytics>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
