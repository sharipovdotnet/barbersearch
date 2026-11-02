using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Индекс под фильтр админского списка броней. Замер
    /// <c>EXPLAIN (ANALYZE, BUFFERS)</c> на 1 200 021 брони и 14 002 пользователях
    /// (<c>barbersearch_load</c>) показал, что порядок <c>start_at DESC, id</c> покрыт
    /// <c>ix_bookings_start_at_id</c> только без фильтра:
    /// <list type="bullet">
    /// <item><c>status = 3</c> (отменённые, 20 %) читал 876 011 строк мимо фильтра и шёл
    /// 3 297 мс — почти вся таблица на первую страницу;</item>
    /// <item><c>status = 1</c> (редкий статус) давал Bitmap Heap Scan и <c>Sort</c>
    /// (117 мс), то есть читал не по порядку выдачи.</item>
    /// </list>
    /// С <c>(status, start_at DESC, id)</c> обе страницы читаются Index Scan без
    /// <c>Sort</c>: 3,4 мс и 0,4 мс, прочитано ровно <c>pageSize + 1</c> строк.
    /// Поэтому индекс добавлен, а не отложен: таблица броней растёт быстро, и её
    /// первая страница не должна зависеть от того, сколько старых строк попадётся
    /// до первого совпадения. Сам <c>ix_bookings_start_at_id</c> остаётся: он
    /// обслуживает выдачу без фильтра.
    /// </remarks>
    public partial class AddAdminBookingsStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DESC по start_at и ASC по id — ровно порядок админского списка; status
            // первым, потому что фильтр статуса стоит в каждом запросе раздела.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_bookings_status_start_at_id
                ON bookings (status, start_at DESC, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_bookings_status_start_at_id;");
        }
    }
}
