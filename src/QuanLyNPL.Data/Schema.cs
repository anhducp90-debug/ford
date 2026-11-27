namespace QuanLyNPL.Data;

/// <summary>Danh sách migration có thứ tự; PRAGMA user_version = số migration đã áp dụng.</summary>
public static class Schema
{
    public static readonly string[] Migrations =
    {
@"
CREATE TABLE customer(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE material(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, unit TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, note TEXT);
CREATE TABLE size_grid(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE size_grid_item(id INTEGER PRIMARY KEY, grid_id INTEGER NOT NULL REFERENCES size_grid(id),
  size_code TEXT NOT NULL, voc TEXT, chieu_cao TEXT, UNIQUE(grid_id,size_code));
CREATE TABLE product(id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customer(id), code TEXT NOT NULL, name TEXT NOT NULL,
  default_grid_id INTEGER NOT NULL REFERENCES size_grid(id), active INTEGER NOT NULL DEFAULT 1, UNIQUE(customer_id,code));
CREATE TABLE variant(id INTEGER PRIMARY KEY, product_id INTEGER NOT NULL REFERENCES product(id), code TEXT NOT NULL, name TEXT NOT NULL,
  group_name TEXT, qty_per_set TEXT NOT NULL CHECK(CAST(qty_per_set AS REAL) > 0), grid_id INTEGER NOT NULL REFERENCES size_grid(id), UNIQUE(product_id,code));
CREATE TABLE size_map(id INTEGER PRIMARY KEY, variant_id INTEGER NOT NULL REFERENCES variant(id),
  default_size_code TEXT NOT NULL, variant_size_code TEXT NOT NULL, UNIQUE(variant_id,default_size_code));

CREATE TABLE bom(id INTEGER PRIMARY KEY, product_id INTEGER NOT NULL REFERENCES product(id), name TEXT NOT NULL, type TEXT,
  scope_variant_id INTEGER REFERENCES variant(id), UNIQUE(product_id,name));
CREATE TABLE bom_version(id INTEGER PRIMARY KEY, bom_id INTEGER NOT NULL REFERENCES bom(id), version_no INTEGER NOT NULL,
  created_at TEXT NOT NULL, UNIQUE(bom_id,version_no));
CREATE TABLE bom_line(id INTEGER PRIMARY KEY, bom_version_id INTEGER NOT NULL REFERENCES bom_version(id),
  material_id INTEGER NOT NULL REFERENCES material(id), size_code TEXT NOT NULL, usage TEXT NOT NULL, waste_percent TEXT NOT NULL DEFAULT '0',
  variant_id INTEGER REFERENCES variant(id));

CREATE TABLE ""order""(id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customer(id), product_id INTEGER NOT NULL REFERENCES product(id),
  order_no TEXT NOT NULL UNIQUE, order_date TEXT NOT NULL, due_date TEXT, status TEXT NOT NULL DEFAULT 'OPEN');
CREATE TABLE order_line(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id), default_size_code TEXT NOT NULL,
  sets_qty TEXT NOT NULL, UNIQUE(order_id,default_size_code));
CREATE TABLE order_snapshot(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id), created_at TEXT NOT NULL, config_json TEXT NOT NULL);

CREATE TABLE stock_doc(id INTEGER PRIMARY KEY, type TEXT NOT NULL CHECK(type IN ('IN','OUT','RETURN','ADJUST_UP','ADJUST_DOWN')),
  doc_no TEXT NOT NULL UNIQUE, status TEXT NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','CONFIRMED')),
  order_id INTEGER REFERENCES ""order""(id), note TEXT, created_at TEXT NOT NULL, confirmed_at TEXT);
CREATE TABLE stock_doc_line(id INTEGER PRIMARY KEY, doc_id INTEGER NOT NULL REFERENCES stock_doc(id),
  material_id INTEGER NOT NULL REFERENCES material(id), qty TEXT NOT NULL);
CREATE TABLE stock_txn(id INTEGER PRIMARY KEY, doc_id INTEGER NOT NULL REFERENCES stock_doc(id),
  material_id INTEGER NOT NULL REFERENCES material(id), qty_signed TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE TABLE reservation(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id),
  material_id INTEGER NOT NULL REFERENCES material(id), qty TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE carry_ledger(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id), material_id INTEGER NOT NULL REFERENCES material(id),
  scope TEXT NOT NULL, opening TEXT NOT NULL, issued TEXT NOT NULL, demand TEXT NOT NULL, closing TEXT NOT NULL, doc_id INTEGER REFERENCES stock_doc(id), created_at TEXT NOT NULL);
CREATE TABLE audit_log(id INTEGER PRIMARY KEY, entity TEXT NOT NULL, entity_id INTEGER, before_json TEXT, after_json TEXT, reason TEXT, created_at TEXT NOT NULL);
CREATE TABLE app_setting(key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE INDEX ix_variant_product ON variant(product_id);
CREATE INDEX ix_bom_product ON bom(product_id);
CREATE INDEX ix_bomline_version ON bom_line(bom_version_id);
CREATE INDEX ix_stocktxn_material ON stock_txn(material_id);
CREATE INDEX ix_order_customer ON ""order""(customer_id);
"
    };
}
