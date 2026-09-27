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
        if (!Schema::hasTable('cms_tags')) {
            Schema::create('cms_tags', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('name', 255)->unique();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('cms_content_tags')) {
            Schema::create('cms_content_tags', function (Blueprint $table) {
                $table->string('content_id', 64)->index();
                $table->string('tag_id', 64)->index();

                $table->primary(['content_id', 'tag_id']);

                $table->foreign('content_id')
                    ->references('id')
                    ->on('contents')
                    ->onDelete('cascade');

                $table->foreign('tag_id')
                    ->references('id')
                    ->on('cms_tags')
                    ->onDelete('cascade');
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('cms_content_tags');
        Schema::dropIfExists('cms_tags');
    }
};
