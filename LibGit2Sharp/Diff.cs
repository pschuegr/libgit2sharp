using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibGit2Sharp.Core;
using LibGit2Sharp.Core.Compat;
using LibGit2Sharp.Core.Handles;
using Environment = System.Environment;

namespace LibGit2Sharp
{
    /// <summary>
    ///   Show changes between the working tree and the index or a tree, changes between the index and a tree, changes between two trees, or changes between two files on disk.
    ///   <para>
    ///     Copied and renamed files currently cannot be detected, as the feature is not supported by libgit2 yet.
    ///     These files will be shown as a pair of Deleted/Added files.</para>
    /// </summary>
    public class Diff
    {
        private readonly Repository repo;

        private static GitDiffOptions BuildOptions(DiffOptions diffOptions, FilePath[] filePaths = null, MatchedPathsAggregator matchedPathsAggregator = null)
        {
            var options = new GitDiffOptions();

            options.Flags |= GitDiffOptionFlags.GIT_DIFF_INCLUDE_TYPECHANGE;
            options.ContextLines = 3;

            if (diffOptions.HasFlag(DiffOptions.IncludeUntracked))
            {
                options.Flags |= GitDiffOptionFlags.GIT_DIFF_INCLUDE_UNTRACKED |
                GitDiffOptionFlags.GIT_DIFF_RECURSE_UNTRACKED_DIRS |
                GitDiffOptionFlags.GIT_DIFF_INCLUDE_UNTRACKED_CONTENT;
            }

            if (diffOptions.HasFlag(DiffOptions.IncludeIgnored))
            {
                options.Flags |= GitDiffOptionFlags.GIT_DIFF_INCLUDE_IGNORED;
            }

            if (diffOptions.HasFlag(DiffOptions.IncludeUnmodified))
            {
                options.Flags |= GitDiffOptionFlags.GIT_DIFF_INCLUDE_UNMODIFIED;
            }

            if (diffOptions.HasFlag(DiffOptions.DisablePathspecMatch))
            {
                options.Flags |= GitDiffOptionFlags.GIT_DIFF_DISABLE_PATHSPEC_MATCH;
            }

            if (matchedPathsAggregator != null)
            {
                options.NotifyCallback = matchedPathsAggregator.OnGitDiffNotify;
            }

            if (filePaths == null)
            {
                return options;
            }

            options.PathSpec = GitStrArrayIn.BuildFrom(filePaths);
            return options;
        }

        private static FilePath[] ToFilePaths(Repository repo, IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return null;
            }

            var filePaths = new List<FilePath>();

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    throw new ArgumentException("At least one provided path is either null or empty.", "paths");
                }

                filePaths.Add(repo.BuildRelativePathFrom(path));
            }

            if (filePaths.Count == 0)
            {
                throw new ArgumentException("No path has been provided.", "paths");
            }

            return filePaths.ToArray();
        }

        /// <summary>
        ///   Needed for mocking purposes.
        /// </summary>
        protected Diff()
        { }

        internal Diff(Repository repo)
        {
            this.repo = repo;
        }

        /// <summary>
        ///   Show changes between two <see cref = "Tree"/>s.
        /// </summary>
        /// <param name = "oldTree">The <see cref = "Tree"/> you want to compare from.</param>
        /// <param name = "newTree">The <see cref = "Tree"/> you want to compare to.</param>
        /// <param name = "paths">The list of paths (either files or directories) that should be compared.</param>
        /// <param name = "explicitPathsOptions">
        ///   If set, the passed <paramref name="paths"/> will be treated as explicit paths.
        ///   Use these options to determine how unmatched explicit paths should be handled.
        /// </param>
        /// <returns>A <see cref = "TreeChanges"/> containing the changes between the <paramref name = "oldTree"/> and the <paramref name = "newTree"/>.</returns>
        public virtual TreeChanges Compare(Tree oldTree, Tree newTree, IEnumerable<string> paths = null, ExplicitPathsOptions explicitPathsOptions = null)
        {
            var comparer = TreeToTree(repo);
            ObjectId oldTreeId = oldTree != null ? oldTree.Id : null;
            ObjectId newTreeId = newTree != null ? newTree.Id : null;
            var diffOptions = DiffOptions.None;

            if (explicitPathsOptions != null)
            {
                diffOptions |= DiffOptions.DisablePathspecMatch;

                if (explicitPathsOptions.ShouldFailOnUnmatchedPath ||
                    explicitPathsOptions.OnUnmatchedPath != null)
                {
                    diffOptions |= DiffOptions.IncludeUnmodified;
                }
            }

            return BuildTreeChangesFromComparer(oldTreeId, newTreeId, comparer, diffOptions, paths, explicitPathsOptions);
        }

        /// <summary>
        ///   Show changes between two <see cref = "Blob"/>s.
        /// </summary>
        /// <param name = "oldBlob">The <see cref = "Blob"/> you want to compare from.</param>
        /// <param name = "newBlob">The <see cref = "Blob"/> you want to compare to.</param>
        /// <returns>A <see cref = "ContentChanges"/> containing the changes between the <paramref name = "oldBlob"/> and the <paramref name = "newBlob"/>.</returns>
        public virtual ContentChanges Compare(Blob oldBlob, Blob newBlob)
        {
            using (GitDiffOptions options = BuildOptions(DiffOptions.None))
            {
                return new ContentChanges(repo, oldBlob, newBlob, options);
            }
        }

        private readonly IDictionary<DiffTargets, Func<Repository, TreeComparisonHandleRetriever>> handleRetrieverDispatcher = BuildHandleRetrieverDispatcher();

        private static IDictionary<DiffTargets, Func<Repository, TreeComparisonHandleRetriever>> BuildHandleRetrieverDispatcher()
        {
            return new Dictionary<DiffTargets, Func<Repository, TreeComparisonHandleRetriever>>
                       {
                           { DiffTargets.Index, IndexToTree },
                           { DiffTargets.WorkingDirectory, WorkdirToTree },
                           { DiffTargets.Index | DiffTargets.WorkingDirectory, WorkdirAndIndexToTree },
                       };
        }

        /// <summary>
        ///   Show changes between a <see cref = "Tree"/> and the Index, the Working Directory, or both.
        /// </summary>
        /// <param name = "oldTree">The <see cref = "Tree"/> to compare from.</param>
        /// <param name = "diffTargets">The targets to compare to.</param>
        /// <param name = "paths">The list of paths (either files or directories) that should be compared.</param>
        /// <param name = "explicitPathsOptions">
        ///   If set, the passed <paramref name="paths"/> will be treated as explicit paths.
        ///   Use these options to determine how unmatched explicit paths should be handled.
        /// </param>
        /// <returns>A <see cref = "TreeChanges"/> containing the changes between the <see cref="Tree"/> and the selected target.</returns>
        public virtual TreeChanges Compare(Tree oldTree, DiffTargets diffTargets, IEnumerable<string> paths = null, ExplicitPathsOptions explicitPathsOptions = null)
        {
            var comparer = handleRetrieverDispatcher[diffTargets](repo);
            ObjectId oldTreeId = oldTree != null ? oldTree.Id : null;

            DiffOptions diffOptions = diffTargets.HasFlag(DiffTargets.WorkingDirectory) ?
                DiffOptions.IncludeUntracked : DiffOptions.None;

            if (explicitPathsOptions != null)
            {
                diffOptions |= DiffOptions.DisablePathspecMatch;

                if (explicitPathsOptions.ShouldFailOnUnmatchedPath ||
                    explicitPathsOptions.OnUnmatchedPath != null)
                {
                    diffOptions |= DiffOptions.IncludeUnmodified;
                }
            }

            return BuildTreeChangesFromComparer(oldTreeId, null, comparer, diffOptions, paths, explicitPathsOptions);
        }

        /// <summary>
        ///   Show changes between the working directory and the index.
        /// </summary>
        /// <param name = "paths">The list of paths (either files or directories) that should be compared.</param>
        /// <param name = "includeUntracked">If true, include untracked files from the working dir as additions. Otherwise ignore them.</param>
        /// <param name = "explicitPathsOptions">
        ///   If set, the passed <paramref name="paths"/> will be treated as explicit paths.
        ///   Use these options to determine how unmatched explicit paths should be handled.
        /// </param>
        /// <returns>A <see cref = "TreeChanges"/> containing the changes between the working directory and the index.</returns>
        public virtual TreeChanges Compare(IEnumerable<string> paths = null, bool includeUntracked = false, ExplicitPathsOptions explicitPathsOptions = null)
        {
            return Compare(includeUntracked ? DiffOptions.IncludeUntracked : DiffOptions.None, paths, explicitPathsOptions);
        }

        internal virtual TreeChanges Compare(DiffOptions diffOptions, IEnumerable<string> paths = null,
                                             ExplicitPathsOptions explicitPathsOptions = null)
        {
            var comparer = WorkdirToIndex(repo);

            if (explicitPathsOptions != null)
            {
                diffOptions |= DiffOptions.DisablePathspecMatch;

                if (explicitPathsOptions.ShouldFailOnUnmatchedPath ||
                    explicitPathsOptions.OnUnmatchedPath != null)
                {
                    diffOptions |= DiffOptions.IncludeUnmodified;
                }
            }

            return BuildTreeChangesFromComparer(null, null, comparer, diffOptions, paths, explicitPathsOptions);
        }

        private delegate DiffListSafeHandle TreeComparisonHandleRetriever(ObjectId oldTreeId, ObjectId newTreeId, GitDiffOptions options);

        private static TreeComparisonHandleRetriever TreeToTree(Repository repo)
        {
            return (oh, nh, o) => Proxy.git_diff_tree_to_tree(repo.Handle, oh, nh, o);
        }

        private static TreeComparisonHandleRetriever WorkdirToIndex(Repository repo)
        {
            return (oh, nh, o) => Proxy.git_diff_index_to_workdir(repo.Handle, repo.Index.Handle, o);
        }

        private static TreeComparisonHandleRetriever WorkdirToTree(Repository repo)
        {
            return (oh, nh, o) => Proxy.git_diff_tree_to_workdir(repo.Handle, oh, o);
        }

        private static TreeComparisonHandleRetriever WorkdirAndIndexToTree(Repository repo)
        {
            TreeComparisonHandleRetriever comparisonHandleRetriever = (oh, nh, o) =>
            {
                DiffListSafeHandle diff = null, diff2 = null;

                try
                {
                    diff = Proxy.git_diff_tree_to_index(repo.Handle, repo.Index.Handle, oh, o);
                    diff2 = Proxy.git_diff_index_to_workdir(repo.Handle, repo.Index.Handle, o);
                    Proxy.git_diff_merge(diff, diff2);
                }
                catch
                {
                    diff.SafeDispose();
                    throw;
                }
                finally
                {
                    diff2.SafeDispose();
                }

                return diff;
            };

            return comparisonHandleRetriever;
        }

        private static TreeComparisonHandleRetriever IndexToTree(Repository repo)
        {
            return (oh, nh, o) => Proxy.git_diff_tree_to_index(repo.Handle, repo.Index.Handle, oh, o);
        }

        private TreeChanges BuildTreeChangesFromComparer(
            ObjectId oldTreeId, ObjectId newTreeId, TreeComparisonHandleRetriever comparisonHandleRetriever,
            DiffOptions diffOptions, IEnumerable<string> paths = null, ExplicitPathsOptions explicitPathsOptions = null)
        {
            var matchedPaths = new MatchedPathsAggregator();
            var filePaths = ToFilePaths(repo, paths);

            using (GitDiffOptions options = BuildOptions(diffOptions, filePaths, matchedPaths))
            using (DiffListSafeHandle diffList = comparisonHandleRetriever(oldTreeId, newTreeId, options))
            {
                if (explicitPathsOptions != null)
                {
                    DispatchUnmatchedPaths(explicitPathsOptions, filePaths, matchedPaths);
                }

                return new TreeChanges(diffList);
            }
        }

        private static void DispatchUnmatchedPaths(ExplicitPathsOptions explicitPathsOptions,
                                                       IEnumerable<FilePath> filePaths,
                                                       IEnumerable<FilePath> matchedPaths)
        {
            List<FilePath> unmatchedPaths = (filePaths != null ?
                filePaths.Except(matchedPaths) : Enumerable.Empty<FilePath>()).ToList();

            if (!unmatchedPaths.Any())
            {
                return;
            }

            if (explicitPathsOptions.OnUnmatchedPath != null)
            {
                unmatchedPaths.ForEach(filePath => explicitPathsOptions.OnUnmatchedPath(filePath.Native));
            }

            if (explicitPathsOptions.ShouldFailOnUnmatchedPath)
            {
                throw new UnmatchedPathException(BuildUnmatchedPathsMessage(unmatchedPaths));
            }
        }

        private static string BuildUnmatchedPathsMessage(List<FilePath> unmatchedPaths)
        {
            var message = new StringBuilder("There were some unmatched paths:" + Environment.NewLine);
            unmatchedPaths.ForEach(filePath => message.AppendFormat("- {0}{1}", filePath.Native, Environment.NewLine));

            return message.ToString();
        }
    }
}
