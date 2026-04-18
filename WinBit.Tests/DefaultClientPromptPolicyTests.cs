using FluentAssertions;
using WinBit.Core.Settings;
using WinBit.Core.Shell;
using Xunit;

namespace WinBit.Tests;

public sealed class DefaultClientPromptPolicyTests
{
    [Fact]
    public void Prompts_when_neither_association_is_ours_and_user_has_not_dismissed()
    {
        DefaultClientPromptPolicy
            .ShouldPrompt(new ShellAssociationStatus(false, false), new BehaviorSettings())
            .Should().BeTrue();
    }

    [Fact]
    public void Prompts_when_one_of_the_two_associations_is_missing()
    {
        var behavior = new BehaviorSettings();
        DefaultClientPromptPolicy
            .ShouldPrompt(new ShellAssociationStatus(true, false), behavior).Should().BeTrue();
        DefaultClientPromptPolicy
            .ShouldPrompt(new ShellAssociationStatus(false, true), behavior).Should().BeTrue();
    }

    [Fact]
    public void Does_not_prompt_when_both_are_already_ours()
    {
        DefaultClientPromptPolicy
            .ShouldPrompt(new ShellAssociationStatus(true, true), new BehaviorSettings())
            .Should().BeFalse();
    }

    [Fact]
    public void Does_not_prompt_when_user_previously_dismissed()
    {
        var behavior = new BehaviorSettings { DefaultClientPromptDismissed = true };
        DefaultClientPromptPolicy
            .ShouldPrompt(new ShellAssociationStatus(false, false), behavior)
            .Should().BeFalse();
    }
}
