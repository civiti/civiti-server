using Civiti.Application.Diffing;
using Civiti.Domain.Entities;
using Civiti.Domain.Snapshots;
using FluentAssertions;

namespace Civiti.Tests.Diffing;

/// <summary>
/// The comparison behind the admin re-review diff. Its job is to be trusted: a field it fails to
/// report is a change a reviewer re-approves without seeing, which is the bait-and-switch the
/// whole snapshot mechanism exists to prevent.
/// </summary>
public class IssueSnapshotDiffTests
{
    private static IssueContentSnapshot Baseline() => new()
    {
        Title = "Groapă pe strada Mihai Eminescu",
        Description = "O groapă adâncă s-a format în asfalt.",
        Category = IssueCategory.Infrastructure,
        Address = "Strada Mihai Eminescu, Nr. 45",
        District = "Sector 2",
        Latitude = 44.4268,
        Longitude = 26.1025,
        Urgency = UrgencyLevel.Medium,
        DesiredOutcome = "Reparație",
        CommunityImpact = "500 de locuitori",
        PhotoUrls = ["https://example.com/a.jpg", "https://example.com/b.jpg"],
        Authorities =
        [
            new IssueAuthoritySnapshot { Name = "Primăria Sector 2", Email = "contact@ps2.ro" },
            new IssueAuthoritySnapshot { Name = "Primăria Sector 3", Email = "contact@ps3.ro" }
        ]
    };

    private static IssueContentSnapshot Modified(Action<IssueContentSnapshot> change)
    {
        IssueContentSnapshot snapshot = Baseline();
        change(snapshot);
        return snapshot;
    }

    [Fact]
    public void Identical_Content_Should_Report_No_Changes()
    {
        IssueSnapshotDiff.Compare(Baseline(), Baseline()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(IssueDiffFields.Title)]
    [InlineData(IssueDiffFields.Description)]
    [InlineData(IssueDiffFields.Category)]
    [InlineData(IssueDiffFields.Address)]
    [InlineData(IssueDiffFields.District)]
    [InlineData(IssueDiffFields.Urgency)]
    [InlineData(IssueDiffFields.DesiredOutcome)]
    [InlineData(IssueDiffFields.CommunityImpact)]
    public void Each_Scalar_Field_Should_Be_Detected(string field)
    {
        IssueContentSnapshot current = Modified(s =>
        {
            switch (field)
            {
                case IssueDiffFields.Title: s.Title = "Something else entirely"; break;
                case IssueDiffFields.Description: s.Description = "Completely different text"; break;
                case IssueDiffFields.Category: s.Category = IssueCategory.Safety; break;
                case IssueDiffFields.Address: s.Address = "Strada Alta, Nr. 1"; break;
                case IssueDiffFields.District: s.District = "Sector 5"; break;
                case IssueDiffFields.Urgency: s.Urgency = UrgencyLevel.Urgent; break;
                case IssueDiffFields.DesiredOutcome: s.DesiredOutcome = "Altceva"; break;
                case IssueDiffFields.CommunityImpact: s.CommunityImpact = "Alt impact"; break;
            }
        });

        IssueSnapshotDiff.Compare(Baseline(), current).Should().Equal([field]);
    }

    [Fact]
    public void Latitude_And_Longitude_Should_Report_As_One_Location_Change()
    {
        // A coordinate is one thing to a reviewer; reporting two fields for one map pin move
        // would just be noise.
        IssueSnapshotDiff.Compare(Baseline(), Modified(s => s.Latitude = 45.0))
            .Should().Equal([IssueDiffFields.Location]);

        IssueSnapshotDiff.Compare(Baseline(), Modified(s =>
            {
                s.Latitude = 45.0;
                s.Longitude = 27.0;
            }))
            .Should().Equal([IssueDiffFields.Location]);
    }

    [Fact]
    public void Null_And_Empty_Should_Be_Treated_As_The_Same_Absent_Value()
    {
        // A legacy issue with a null district that the owner leaves blank has not changed.
        IssueContentSnapshot approved = Modified(s => s.District = null);
        IssueContentSnapshot current = Modified(s => s.District = string.Empty);

        IssueSnapshotDiff.Compare(approved, current).Should().BeEmpty();
    }

    [Fact]
    public void Reordering_Photos_Should_Count_As_A_Change()
    {
        // Index 0 is the primary photo, so a reorder changes what the public sees.
        IssueContentSnapshot current = Modified(s =>
            s.PhotoUrls = ["https://example.com/b.jpg", "https://example.com/a.jpg"]);

        IssueSnapshotDiff.Compare(Baseline(), current).Should().Equal([IssueDiffFields.Photos]);
    }

    [Fact]
    public void Adding_Or_Removing_A_Photo_Should_Count_As_A_Change()
    {
        IssueSnapshotDiff.Compare(Baseline(), Modified(s => s.PhotoUrls.RemoveAt(1)))
            .Should().Equal([IssueDiffFields.Photos]);

        IssueSnapshotDiff.Compare(Baseline(), Modified(s => s.PhotoUrls.Add("https://example.com/c.jpg")))
            .Should().Equal([IssueDiffFields.Photos]);
    }

    [Fact]
    public void Reordering_Authorities_Should_Not_Count_As_A_Change()
    {
        // Which authorities are targeted is meaningful; the order they are listed in is not.
        IssueContentSnapshot current = Modified(s => s.Authorities.Reverse());

        IssueSnapshotDiff.Compare(Baseline(), current).Should().BeEmpty();
    }

    [Fact]
    public void Authority_Email_Casing_Should_Not_Count_As_A_Change()
    {
        IssueContentSnapshot current = Modified(s => s.Authorities[0].Email = "CONTACT@PS2.RO");

        IssueSnapshotDiff.Compare(Baseline(), current).Should().BeEmpty();
    }

    [Fact]
    public void Swapping_An_Authority_Should_Count_As_A_Change()
    {
        // The case that matters most: quietly redirecting an approved petition elsewhere.
        IssueContentSnapshot current = Modified(s =>
            s.Authorities[1] = new IssueAuthoritySnapshot
            {
                Name = "Altă instituție",
                Email = "altceva@example.ro"
            });

        IssueSnapshotDiff.Compare(Baseline(), current).Should().Equal([IssueDiffFields.Authorities]);
    }

    [Fact]
    public void Renaming_A_Predefined_Authority_Should_Count_As_A_Change()
    {
        IssueContentSnapshot current = Modified(s => s.Authorities[0].Name = "Primăria Sectorului 2");

        IssueSnapshotDiff.Compare(Baseline(), current).Should().Equal([IssueDiffFields.Authorities]);
    }

    [Fact]
    public void Several_Simultaneous_Changes_Should_All_Be_Reported()
    {
        IssueContentSnapshot current = Modified(s =>
        {
            s.Title = "Different";
            s.Latitude = 45.0;
            s.PhotoUrls.Clear();
        });

        IssueSnapshotDiff.Compare(Baseline(), current).Should().BeEquivalentTo(
            [IssueDiffFields.Title, IssueDiffFields.Location, IssueDiffFields.Photos]);
    }
}
