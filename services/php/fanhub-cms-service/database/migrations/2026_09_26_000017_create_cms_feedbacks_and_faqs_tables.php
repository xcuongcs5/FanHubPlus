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
        if (!Schema::hasTable('cms_feedbacks')) {
            Schema::create('cms_feedbacks', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('user_id', 64)->nullable()->index();
                $table->string('user_name', 255)->nullable();
                $table->string('user_email', 255)->nullable();
                $table->string('type', 50)->default('bug')->index();
                $table->string('title', 255);
                $table->text('content')->nullable();
                $table->string('screenshot_url', 1000)->nullable();
                $table->string('status', 50)->default('Open')->index();
                $table->text('response_note')->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('cms_faqs')) {
            Schema::create('cms_faqs', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('question', 500);
                $table->text('answer');
                $table->string('category', 100)->nullable()->index();
                $table->boolean('is_active')->default(true);
                $table->timestamps();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('cms_faqs');
        Schema::dropIfExists('cms_feedbacks');
    }
};
