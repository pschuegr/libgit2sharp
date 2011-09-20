﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp.Core;
using LibGit2Sharp.Tests.TestHelpers;
using NUnit.Framework;

namespace LibGit2Sharp.Tests
{
    [TestFixture]
    public class CommitFixture : BaseFixture
    {
        private const string sha = "8496071c1b46c854b31185ea97743be6a8774479";
        private readonly List<string> expectedShas = new List<string> { "a4a7d", "c4780", "9fd73", "4a202", "5b5b0", "84960" };

        [Test]
        public void CanCountCommits()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                repo.Commits.Count().ShouldEqual(7);
            }
        }

        [Test]
        public void CanCorrectlyCountCommitsWhenSwitchingToAnotherBranch()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                repo.Branches.Checkout("test");
                repo.Commits.Count().ShouldEqual(2);
                repo.Commits.First().Id.Sha.ShouldEqual("e90810b8df3e80c413d903f631643c716887138d");

                repo.Branches.Checkout("master");
                repo.Commits.Count().ShouldEqual(7);
                repo.Commits.First().Id.Sha.ShouldEqual("4c062a6361ae6959e06292c1fa5e2822d9c96345");
            }
        }

        [Test]
        public void CanEnumerateCommits()
        {
            int count = 0;
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                foreach (Commit commit in repo.Commits)
                {
                    commit.ShouldNotBeNull();
                    count++;
                }
            }
            count.ShouldEqual(7);
        }

        [Test]
        public void CanEnumerateCommitsInDetachedHeadState()
        {
            TemporaryCloneOfTestRepo path = BuildTemporaryCloneOfTestRepo();
            using (var repo = new Repository(path.RepositoryPath))
            {
                ObjectId parentOfHead = repo.Head.Tip.Parents.First().Id;

                repo.Refs.Create("HEAD", parentOfHead.Sha, true);
                Assert.AreEqual(true, repo.Info.IsHeadDetached);

                repo.Commits.Count().ShouldEqual(6);
            }
        }

        [Test]
        public void DefaultOrderingWhenEnumeratingCommitsIsTimeBased()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                repo.Commits.SortedBy.ShouldEqual(GitSortOptions.Time);
            }
        }

        [Test]
        public void CanEnumerateCommitsFromSha()
        {
            int count = 0;
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                foreach (Commit commit in repo.Commits.QueryBy(new Filter { Since = "a4a7dce85cf63874e984719f4fdd239f5145052f" }))
                {
                    commit.ShouldNotBeNull();
                    count++;
                }
            }
            count.ShouldEqual(6);
        }

        [Test]
        public void QueryingTheCommitHistoryWithUnknownShaOrInvalidEntryPointThrows()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                Assert.Throws<LibGit2Exception>(() => repo.Commits.QueryBy(new Filter { Since = Constants.UnknownSha }).Count());
                Assert.Throws<LibGit2Exception>(() => repo.Commits.QueryBy(new Filter { Since = "refs/heads/deadbeef" }).Count());
                Assert.Throws<ArgumentNullException>(() => repo.Commits.QueryBy(new Filter { Since = null }).Count());
            }
        }

        [Test]
        public void QueryingTheCommitHistoryFromACorruptedReferenceThrows()
        {
            TemporaryCloneOfTestRepo path = BuildTemporaryCloneOfTestRepo();
            using (var repo = new Repository(path.RepositoryPath))
            {
                CreateCorruptedDeadBeefHead(repo.Info.Path);

                Assert.Throws<LibGit2Exception>(() => repo.Commits.QueryBy(new Filter { Since = repo.Branches["deadbeef"] }).Count());
                Assert.Throws<LibGit2Exception>(() => repo.Commits.QueryBy(new Filter { Since = repo.Refs["refs/heads/deadbeef"] }).Count());
            }
        }

        [Test]
        public void QueryingTheCommitHistoryWithBadParamsThrows()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                Assert.Throws<ArgumentException>(() => repo.Commits.QueryBy(new Filter { Since = string.Empty }));
                Assert.Throws<ArgumentNullException>(() => repo.Commits.QueryBy(new Filter { Since = null }));
                Assert.Throws<ArgumentNullException>(() => repo.Commits.QueryBy(null));
            }
        }

        [Test]
        public void CanEnumerateCommitsWithReverseTimeSorting()
        {
            expectedShas.Reverse();
            int count = 0;
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                foreach (Commit commit in repo.Commits.QueryBy(new Filter { Since = "a4a7dce85cf63874e984719f4fdd239f5145052f", SortBy = GitSortOptions.Time | GitSortOptions.Reverse }))
                {
                    commit.ShouldNotBeNull();
                    commit.Sha.StartsWith(expectedShas[count]);
                    count++;
                }
            }
            count.ShouldEqual(6);
        }

        [Test]
        public void CanEnumerateCommitsWithReverseTopoSorting()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                List<Commit> commits = repo.Commits.QueryBy(new Filter { Since = "a4a7dce85cf63874e984719f4fdd239f5145052f", SortBy = GitSortOptions.Time | GitSortOptions.Reverse }).ToList();
                foreach (Commit commit in commits)
                {
                    commit.ShouldNotBeNull();
                    foreach (Commit p in commit.Parents)
                    {
                        Commit parent = commits.Single(x => x.Id == p.Id);
                        Assert.Greater(commits.IndexOf(commit), commits.IndexOf(parent));
                    }
                }
            }
        }

        [Test]
        public void CanEnumerateCommitsWithTimeSorting()
        {
            int count = 0;
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                foreach (Commit commit in repo.Commits.QueryBy(new Filter { Since = "a4a7dce85cf63874e984719f4fdd239f5145052f", SortBy = GitSortOptions.Time }))
                {
                    commit.ShouldNotBeNull();
                    commit.Sha.StartsWith(expectedShas[count]);
                    count++;
                }
            }
            count.ShouldEqual(6);
        }

        [Test]
        public void CanEnumerateCommitsWithTopoSorting()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                List<Commit> commits = repo.Commits.QueryBy(new Filter { Since = "a4a7dce85cf63874e984719f4fdd239f5145052f", SortBy = GitSortOptions.Topological }).ToList();
                foreach (Commit commit in commits)
                {
                    commit.ShouldNotBeNull();
                    foreach (Commit p in commit.Parents)
                    {
                        Commit parent = commits.Single(x => x.Id == p.Id);
                        Assert.Less(commits.IndexOf(commit), commits.IndexOf(parent));
                    }
                }
            }
        }

        [Test]
        public void CanEnumerateUsingTwoHeadsAsBoundaries()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = "HEAD", Until = "refs/heads/br2" },
                new[] { "4c062a6", "be3563a" }
                );
        }

        [Test]
        public void CanEnumerateUsingOneHeadAsBoundaries()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Until = "refs/heads/br2" },
                new[] { "4c062a6", "be3563a" }
                );
        }

        [Test]
        public void CanEnumerateUsingTwoAbbreviatedShasAsBoundaries()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = "a4a7dce", Until = "4a202b3" },
                new[] { "a4a7dce", "c47800c", "9fd738e" }
                );
        }

        [Test]
        public void CanEnumerateCommitsFromTwoHeads()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = new[] { "refs/heads/br2", "refs/heads/master" } },
                new[]
                    {
                        "4c062a6", "a4a7dce", "be3563a", "c47800c",
                        "9fd738e", "4a202b3", "5b5b025", "8496071",
                    });
        }

        [Test]
        public void CanEnumerateCommitsFromMixedStartingPoints()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = new object[] { repo.Branches["br2"], "refs/heads/master", new ObjectId("e90810b") } },
                new[]
                    {
                        "4c062a6", "e90810b", "6dcf9bf", "a4a7dce",
                        "be3563a", "c47800c", "9fd738e", "4a202b3",
                        "5b5b025", "8496071",
                    });
        }

        [Test]
        public void CanEnumerateCommitsFromAnAnnotatedTag()
        {
            CanEnumerateCommitsFromATag(t => t);
        }

        [Test]
        public void CanEnumerateCommitsFromATagAnnotation()
        {
            CanEnumerateCommitsFromATag(t => t.Annotation);
        }

        private static void CanEnumerateCommitsFromATag(Func<Tag, object> transformer)
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = transformer(repo.Tags["test"]) },
                new[] { "e90810b", "6dcf9bf", }
                );
        }

        [Test]
        public void CanEnumerateAllCommits()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = repo.Refs },
                new[]
                    {
                        "4c062a6", "e90810b", "6dcf9bf", "a4a7dce",
                        "be3563a", "c47800c", "9fd738e", "4a202b3",
                        "41bc8c6", "5001298", "5b5b025", "8496071",
                    });
        }

        [Test]
        public void CanEnumerateCommitsFromATagWhichDoesNotPointAtACommit()
        {
            AssertEnumerationOfCommits(
                repo => new Filter { Since = repo.Tags["point_to_blob"] },
                new string[] { });
        }

        private static void AssertEnumerationOfCommits(Func<Repository, Filter> filterBuilder, IEnumerable<string> abbrevIds)
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                ICommitCollection commits = repo.Commits.QueryBy(filterBuilder(repo));

                IEnumerable<string> commitShas = commits.Select(c => c.Id.ToString(7)).ToArray();

                CollectionAssert.AreEqual(abbrevIds, commitShas);
            }
        }

        [Test]
        public void CanLookupCommitGeneric()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                var commit = repo.Lookup<Commit>(sha);
                commit.Message.ShouldEqual("testing\n");
                commit.Sha.ShouldEqual(sha);
            }
        }

        [Test]
        public void CanReadCommitData()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                GitObject obj = repo.Lookup(sha);
                obj.ShouldNotBeNull();
                obj.GetType().ShouldEqual(typeof(Commit));

                var commit = (Commit)obj;
                commit.Message.ShouldEqual("testing\n");
                commit.Encoding.ShouldEqual("UTF-8");
                commit.Sha.ShouldEqual(sha);

                commit.Author.ShouldNotBeNull();
                commit.Author.Name.ShouldEqual("Scott Chacon");
                commit.Author.Email.ShouldEqual("schacon@gmail.com");
                commit.Author.When.ToSecondsSinceEpoch().ShouldEqual(1273360386);

                commit.Committer.ShouldNotBeNull();
                commit.Committer.Name.ShouldEqual("Scott Chacon");
                commit.Committer.Email.ShouldEqual("schacon@gmail.com");
                commit.Committer.When.ToSecondsSinceEpoch().ShouldEqual(1273360386);

                commit.Tree.Sha.ShouldEqual("181037049a54a1eb5fab404658a3a250b44335d7");

                commit.Parents.Count().ShouldEqual(0);
            }
        }

        [Test]
        public void CanReadCommitWithMultipleParents()
        {
            using (var repo = new Repository(Constants.BareTestRepoPath))
            {
                var commit = repo.Lookup<Commit>("a4a7dce85cf63874e984719f4fdd239f5145052f");
                commit.Parents.Count().ShouldEqual(2);
            }
        }

        [Test]
        public void CanCommitALittleBit()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            string dir = Repository.Init(scd.DirectoryPath);
            Path.IsPathRooted(dir).ShouldBeTrue();
            Directory.Exists(dir).ShouldBeTrue();

            using (var repo = new Repository(dir))
            {
                string filePath = Path.Combine(repo.Info.WorkingDirectory, "new.txt");

                File.WriteAllText(filePath, "null");
                repo.Index.Stage("new.txt");
                File.AppendAllText(filePath, "token\n");
                repo.Index.Stage("new.txt");

                var author = new Signature("Author N. Ame", "him@there.com", DateTimeOffset.Now.AddSeconds(-10));
                Commit commit = repo.Commit(author, author, "Initial egotistic commit");

                commit.Parents.Count().ShouldEqual(0);
                repo.Info.IsEmpty.ShouldBeFalse();

                File.WriteAllText(filePath, "nulltoken commits!\n");
                repo.Index.Stage("new.txt");

                var author2 = new Signature(author.Name, author.Email, author.When.AddSeconds(5));
                Commit commit2 = repo.Commit(author2, author2, "Are you trying to fork me?");

                commit2.Parents.Count().ShouldEqual(1);
                commit2.Parents.First().Id.ShouldEqual(commit.Id);

                repo.CreateBranch("davidfowl-rules", commit.Id.Sha); //TODO: This cries for a shortcut method :-/
                repo.Branches.Checkout("davidfowl-rules"); //TODO: This cries for a shortcut method :-/

                File.WriteAllText(filePath, "davidfowl commits!\n");

                var author3 = new Signature("David Fowler", "david.fowler@microsoft.com", author.When.AddSeconds(2));
                repo.Index.Stage("new.txt");

                Commit commit3 = repo.Commit(author3, author3, "I'm going to branch you backwards in time!");

                commit3.Parents.Count().ShouldEqual(1);
                commit3.Parents.First().Id.ShouldEqual(commit.Id);
            }
        }
    }
}
