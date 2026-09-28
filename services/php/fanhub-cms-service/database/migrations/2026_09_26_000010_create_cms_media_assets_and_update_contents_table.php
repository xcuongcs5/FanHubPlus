<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        // 1. Cập nhật bảng contents (cms_contents)
        $contentTables = array_filter(['contents', 'cms_contents'], fn($t) => Schema::hasTable($t));
        foreach ($contentTables as $tbl) {
            Schema::table($tbl, function (Blueprint $table) use ($tbl) {
                if (!Schema::hasColumn($tbl, 'is_featured')) {
                    $table->boolean('is_featured')->default(false)->after('status');
                }
                if (!Schema::hasColumn($tbl, 'is_pinned')) {
                    $table->boolean('is_pinned')->default(false)->after('is_featured');
                }
                if (!Schema::hasColumn($tbl, 'reject_reason')) {
                    $table->text('reject_reason')->nullable()->after('is_pinned');
                }
            });
        }

        // 2. Tạo bảng cms_media_assets
        if (!Schema::hasTable('cms_media_assets')) {
            Schema::create('cms_media_assets', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('content_id', 64)->index();
                $table->string('file_url', 500);
                $table->string('file_type', 20)->default('Image');
                $table->integer('sort_order')->default(0);
                $table->timestamp('created_at')->nullable()->useCurrent();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('cms_media_assets');

        $contentTables = array_filter(['contents', 'cms_contents'], fn($t) => Schema::hasTable($t));
        foreach ($contentTables as $tbl) {
            Schema::table($tbl, function (Blueprint $table) use ($tbl) {
                if (Schema::hasColumn($tbl, 'is_featured')) {
                    $table->dropColumn('is_featured');
                }
                if (Schema::hasColumn($tbl, 'is_pinned')) {
                    $table->dropColumn('is_pinned');
                }
                if (Schema::hasColumn($tbl, 'reject_reason')) {
                    $table->dropColumn('reject_reason');
                }
            });
        }
    }
};
