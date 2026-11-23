using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// What turns a page router into a pool's (MP-P8): the pool's data and actions, and the pool page.
/// With it, <c>/</c> draws the pool page, <c>/pause</c>, <c>/drain</c> and <c>/resume</c> act on the
/// project named or on every project, <c>/stopall</c> on every project, and every route that names a
/// card or a slot needs <c>project=</c> and goes to that project's own data and actions.
/// </summary>
/// <param name="Data">The pool's data; also the router's <see cref="IPageData"/>.</param>
/// <param name="Actions">The pool's actions, per project and pool-wide.</param>
/// <param name="Renderer">The pool page.</param>
public sealed record PoolRouting(IPoolPageData Data, IPoolPageActions Actions, IPoolPageRenderer Renderer);
