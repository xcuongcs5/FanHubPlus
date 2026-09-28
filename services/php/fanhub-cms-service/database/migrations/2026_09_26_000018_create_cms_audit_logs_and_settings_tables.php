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
        if (!Schema::hasTable('cms_audit_logs')) {
            Schema::create('cms_audit_logs', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('actor_id', 64)->nullable()->index();
                $table->string('actor', 255)->default('Admin Van Gioi');
                $table->string('action', 100)->default('Ban_User')->index();
                $table->string('target', 255)->nullable();
                $table->timestamp('timestamp')->nullable()->index();
                $table->string('ip', 50)->default('14.161.x.x');
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('cms_system_settings')) {
            Schema::create('cms_system_settings', function (Blueprint $table) {
                $table->id();
                $table->boolean('maintenance_mode')->default(false);
                $table->double('platform_commission_fee', 8, 2)->default(5.0);
                $table->integer('max_upload_size_mb')->default(25);
                $table->timestamps();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('cms_system_settings');
        Schema::dropIfExists('cms_audit_logs');
    }
};
