using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace QuizPollApi.Infrastructure.Persistence.Configurations;

public sealed class LowerEnumConverter<T>() : ValueConverter<T, string>(
    v => v.ToString().ToLowerInvariant(),
    v => Enum.Parse<T>(v, true)) where T : struct, Enum;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Country).HasMaxLength(2);
        b.Property(x => x.AgeGroup).HasMaxLength(20);
        b.Property(x => x.Gender).HasMaxLength(30);
    }
}

public sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> b)
    {
        b.ToTable("quizzes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Type).HasConversion<LowerEnumConverter<QuizType>>().HasMaxLength(20);
        b.Property(x => x.Visibility).HasConversion<LowerEnumConverter<QuizVisibility>>().HasMaxLength(20);
        b.Property(x => x.ResultVisibility).HasConversion<LowerEnumConverter<ResultVisibility>>().HasMaxLength(20);
        b.Property(x => x.AccessCode).HasMaxLength(32);
        b.Ignore(x => x.Status);

        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Questions).WithOne(x => x.Quiz).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.CreatorId);
        b.HasIndex(x => new { x.Visibility, x.IsActive, x.CreatedAt });
    }
}

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.ToTable("questions");
        b.HasKey(x => x.Id);
        b.Property(x => x.QuestionText).HasMaxLength(500).IsRequired();
        b.Property(x => x.QuestionType).HasConversion<LowerEnumConverter<QuestionType>>().HasMaxLength(20);
        b.Property(x => x.ImageUrl).HasMaxLength(2048);
        b.HasMany(x => x.Options).WithOne(x => x.Question).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.QuizId, x.OrderIndex });
    }
}

public sealed class AnswerOptionConfiguration : IEntityTypeConfiguration<AnswerOption>
{
    public void Configure(EntityTypeBuilder<AnswerOption> b)
    {
        b.ToTable("answer_options");
        b.HasKey(x => x.Id);
        b.Property(x => x.OptionText).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.QuestionId);
    }
}

public sealed class ResponseConfiguration : IEntityTypeConfiguration<Response>
{
    public void Configure(EntityTypeBuilder<Response> b)
    {
        b.ToTable("responses");
        b.HasKey(x => x.Id);
        b.Property(x => x.SessionId).HasMaxLength(64);
        b.Property(x => x.IpAddressHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Answers).WithOne(x => x.Response).HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.QuizId, x.UserId }).IsUnique().HasFilter("user_id IS NOT NULL");
        b.HasIndex(x => new { x.QuizId, x.SessionId }).IsUnique().HasFilter("session_id IS NOT NULL");
        b.HasIndex(x => new { x.QuizId, x.IpAddressHash }).IsUnique().HasFilter("user_id IS NULL");
        b.HasIndex(x => new { x.QuizId, x.CompletedAt });
    }
}

public sealed class ResponseAnswerConfiguration : IEntityTypeConfiguration<ResponseAnswer>
{
    public void Configure(EntityTypeBuilder<ResponseAnswer> b)
    {
        b.ToTable("response_answers");
        b.HasKey(x => x.Id);
        b.Property(x => x.TextAnswer).HasMaxLength(2000);
        b.Property(x => x.SelectedOptionIds).HasColumnType("integer[]");
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ResponseId, x.QuestionId }).IsUnique();
        b.HasIndex(x => x.QuestionId);
    }
}

public sealed class QuizAnalyticsConfiguration : IEntityTypeConfiguration<QuizAnalytics>
{
    public void Configure(EntityTypeBuilder<QuizAnalytics> b)
    {
        b.ToTable("quiz_analytics");
        b.HasKey(x => x.Id);
        b.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.QuizId).IsUnique(); 
    }
}
