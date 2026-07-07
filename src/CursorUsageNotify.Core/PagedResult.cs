
namespace Larpx.PersonalTools.CursorUsageNotify.Core
{
    /// <summary>
    /// 分页查询结果，包含当前页数据与总记录数。
    /// </summary>
    /// <typeparam name="T">
    /// 数据项类型。
    /// </typeparam>
    public sealed class PagedResult<T>
    {
        /// <summary>
        /// 当前页数据。
        /// </summary>
        public List<T> Items { get; init; } = [];

        /// <summary>
        /// 总记录数。
        /// </summary>
        public int TotalCount { get; init; }

        /// <summary>
        /// 当前页码（从 1 开始）。
        /// </summary>
        public int PageIndex { get; init; }

        /// <summary>
        /// 每页大小。
        /// </summary>
        public int PageSize { get; init; }

        /// <summary>
        /// 总页数。
        /// </summary>
        public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

        /// <summary>
        /// 是否有上一页。
        /// </summary>
        public bool HasPrev => PageIndex > 1;

        /// <summary>
        /// 是否有下一页。
        /// </summary>
        public bool HasNext => PageIndex < TotalPages;
    }
}
