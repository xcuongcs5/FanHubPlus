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
        Schema::create('fandom_group_members', function (Blueprint $table) {
            $table->string('id', 64)->primary();
            $table->string('group_id', 64)->index();
            $table->string('user_id', 64)->index();
            $table->string('role', 50)->default('member');
            $table->string('status', 50)->default('active');
            $table->timestamp('joined_at')->useCurrent();

            $table->unique(['group_id', 'user_id'], 'group_user_unique');

            $table->foreign('group_id')
                ->references('id')
                ->on('fandom_groups')
                ->onDelete('cascade');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('fandom_group_members');
    }
};
