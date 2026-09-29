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
        if (!Schema::hasTable('cms_character_profiles')) {
            Schema::create('cms_character_profiles', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('category_id', 64)->nullable()->index();
                $table->string('name', 255);
                $table->text('biography')->nullable();
                $table->string('avatar_url', 500)->nullable();
                $table->timestamps();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('cms_character_profiles');
    }
};
